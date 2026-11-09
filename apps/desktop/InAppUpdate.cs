using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace EduCast.Studio;

internal sealed class StagedUpdate(string folder, AvailableUpdate release) : IDisposable
{
    private bool transferred;
    internal string Folder { get; } = folder;
    internal AvailableUpdate Release { get; } = release;
    internal void Transfer() => transferred = true;

    public void Dispose()
    {
        if (!transferred)
            InAppUpdate.Clean(Folder);
    }
}

internal static class InAppUpdate
{
    internal const string RunnerArgument = "--apply-update";
    private const string InstallerName = "update.msi";
    private const string RequestName = "request.json";
    private const string Finished = "finished";
    private const string Executable = "EduCast.Studio.exe";
    private static readonly string[] RunnerFiles =
    [
        Executable, "EduCast.Studio.dll", "EduCast.Studio.deps.json",
        "EduCast.Studio.runtimeconfig.json", "EduCast.Classroom.dll"
    ];
    private static string StagingRoot => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "EduCast Studio Updates"));
    private sealed record UpdateRequest(int ParentId, long ParentStarted, Version Version, long Size, string Sha256,
        string SourceFolder);

    internal static async Task<StagedUpdate> StageAsync(AvailableUpdate release, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StagingRoot);
        var folder = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            if (!IsStage(folder))
                throw new InvalidDataException("The Windows update staging folder is invalid.");
            var installer = Path.Combine(folder, InstallerName);
            await UpdateDownload.SaveAsync(release, installer, cancellationToken).ConfigureAwait(false);
            MsiUpdatePackage.Verify(installer, release.Version);
            cancellationToken.ThrowIfCancellationRequested();
            return new StagedUpdate(folder, release);
        }
        catch
        {
            Clean(folder);
            throw;
        }
    }

    internal static void StartRunner(StagedUpdate update)
    {
        var folder = update.Folder;
        foreach (var name in RunnerFiles)
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(folder, name));
        using var parent = Process.GetCurrentProcess();
        var request = new UpdateRequest(parent.Id, parent.StartTime.ToUniversalTime().Ticks, update.Release.Version,
            update.Release.Size, update.Release.Sha256, Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        File.WriteAllBytes(Path.Combine(folder, RequestName), JsonSerializer.SerializeToUtf8Bytes(request));
        var start = new ProcessStartInfo(Path.Combine(folder, Executable))
        {
            WorkingDirectory = folder,
            UseShellExecute = false
        };
        start.ArgumentList.Add(RunnerArgument);
        using var runner = Process.Start(start) ?? throw new IOException("Could not start the EduCast updater.");
        update.Transfer();
    }

    internal static async Task<bool> ApplyAsync()
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        if (!IsStage(folder))
            throw new InvalidDataException("EduCast can only install from its verified update staging folder.");

        string installedApp;
        try
        {
            var requestPath = Path.Combine(folder, RequestName);
            if (new FileInfo(requestPath).Length > 4096)
                throw new InvalidDataException("The update request exceeds its size limit.");
            var request = JsonSerializer.Deserialize<UpdateRequest>(await File.ReadAllBytesAsync(requestPath));
            if (request is null || request.ParentId <= 0 || request.ParentId == Environment.ProcessId ||
                request.Version is null || request.Version <= ReleaseCatalog.InstalledVersion ||
                request.Size is <= 0 or > ReleaseCatalog.MaxInstallerBytes || !ReleaseCatalog.IsSha256(request.Sha256))
                throw new InvalidDataException("The staged update request is invalid.");

            await WaitForParentAsync(request.ParentId, request.ParentStarted);
            var package = Path.Combine(folder, InstallerName);
            await VerifyStagedFileAsync(package, request.Size, request.Sha256);
            MsiUpdatePackage.Verify(package, request.Version);

            using var installer = Process.Start(InstallerCommand(package)) ??
                throw new IOException("Could not start Windows Installer.");
            await installer.WaitForExitAsync();
            if (installer.ExitCode == 3010)
                return false;
            if (installer.ExitCode != 0)
                throw new IOException($"Windows Installer could not finish the update (error {installer.ExitCode}).");
            installedApp = InstalledExecutable(request.Version);
            CleanOldPortable(request.SourceFolder, Path.GetDirectoryName(installedApp)!);
        }
        finally
        {
            UpdateDownload.TryDelete(Path.Combine(folder, InstallerName));
            UpdateDownload.TryDelete(Path.Combine(folder, RequestName));
            try { File.WriteAllText(Path.Combine(folder, Finished), ""); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Clean(folder);
        }
        Reopen(installedApp);
        return true;
    }

    internal static ProcessStartInfo InstallerCommand(string package)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
        {
            UseShellExecute = false
        };
        foreach (var argument in new[] { "/i", package, "/passive", "/norestart" })
            start.ArgumentList.Add(argument);
        return start;
    }

    internal static async Task VerifyStagedFileAsync(string package, long expectedLength, string digest)
    {
        await using var file = File.OpenRead(package);
        if (file.Length != expectedLength || !ReleaseCatalog.IsSha256(digest) ||
            !CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(file), Convert.FromHexString(digest)))
            throw new InvalidDataException("The staged installer failed its checksum check.");
    }

    private static async Task WaitForParentAsync(int processId, long started)
    {
        Process parent;
        try { parent = Process.GetProcessById(processId); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            try
            {
                if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != started)
                    return;
            }
            catch (InvalidOperationException) { return; }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await parent.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                throw new IOException("EduCast is still open. Close the app before trying the update again.");
            }
        }
    }

    private static string InstalledExecutable(Version expected)
    {
        var installFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "EduCast Studio");
        var app = Path.Combine(installFolder, Executable);
        if (!File.Exists(app) || ReadAppVersion(installFolder) != expected)
            throw new IOException("EduCast could not verify the installed version. Open EduCast Studio from Start to check the update.");
        return app;
    }

    private static void Reopen(string app)
    {
        using var process = Process.Start(new ProcessStartInfo(app)
        {
            WorkingDirectory = Path.GetDirectoryName(app)!,
            UseShellExecute = false
        }) ?? throw new IOException("The update installed. Open EduCast Studio from Start to finish.");
    }

    private static Version? ReadAppVersion(string folder)
    {
        var assembly = Path.Combine(folder, "EduCast.Studio.dll");
        if (!File.Exists(assembly))
            return null;
        var identity = AssemblyName.GetAssemblyName(assembly);
        return identity.Name == "EduCast.Studio" && identity.Version is { } version
            ? new Version(version.Major, version.Minor, version.Build) : null;
    }

    private static void CleanOldPortable(string? source, string installed)
    {
        if (string.IsNullOrEmpty(source) || !Path.IsPathFullyQualified(source))
            return;
        try
        {
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            if (string.Equals(folder, installed, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(folder) || IsReparsePoint(folder) ||
                ReadAppVersion(folder) != ReleaseCatalog.InstalledVersion)
                return;
            foreach (var name in RunnerFiles)
                UpdateDownload.TryDelete(Path.Combine(folder, name));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (BadImageFormatException) { }
    }

    internal static void CleanFinished()
    {
        try
        {
            if (!Directory.Exists(StagingRoot) || IsReparsePoint(StagingRoot))
                return;
            foreach (var folder in Directory.EnumerateDirectories(StagingRoot))
            {
                if (IsStage(folder) && File.Exists(Path.Combine(folder, Finished)))
                    Clean(folder);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void Clean(string folder)
    {
        try
        {
            if (!IsStage(folder))
                return;
            foreach (var name in RunnerFiles.Append(InstallerName).Append(RequestName))
                UpdateDownload.TryDelete(Path.Combine(folder, name));
            if (Directory.EnumerateFileSystemEntries(folder).All(path => Path.GetFileName(path) == Finished))
            {
                UpdateDownload.TryDelete(Path.Combine(folder, Finished));
                Directory.Delete(folder);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsStage(string folder) =>
        string.Equals(Path.GetDirectoryName(folder), StagingRoot, StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParseExact(Path.GetFileName(folder), "N", out _) &&
        Directory.Exists(folder) && !IsReparsePoint(StagingRoot) && !IsReparsePoint(folder);

    private static bool IsReparsePoint(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
