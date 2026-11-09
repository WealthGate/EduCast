using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EduCast.Studio;

internal sealed record CameraDevice(string Name, string Id);

internal static partial class FfmpegMedia
{
    internal static string? Find()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = directory.Trim().Trim('"');
            if (Path.IsPathFullyQualified(path) && File.Exists(Path.Combine(path, "ffmpeg.exe")))
                return Path.Combine(path, "ffmpeg.exe");
        }
        return null;
    }

    internal static Process Start(string executable, IEnumerable<string> args, bool input = false, bool output = false)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            RedirectStandardInput = input, RedirectStandardOutput = output
        };
        foreach (var argument in args)
            info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new IOException("Could not start FFmpeg.");
    }

    internal static async Task<IReadOnlyList<CameraDevice>> CamerasAsync(string executable)
    {
        using var process = Start(executable, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"]);
        var output = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return ParseCameras(await output.ConfigureAwait(false));
        }
        finally { Stop(process); }
    }

    internal static IReadOnlyList<CameraDevice> ParseCameras(string listing)
    {
        var cameras = new List<CameraDevice>();
        foreach (Match match in CameraLine().Matches(listing))
        {
            var id = match.Groups["id"].Value;
            if (match.Groups["kind"].Value == "video" ||
                (match.Groups["kind"].Value == "none" && id.Contains("{65e8773d-", StringComparison.OrdinalIgnoreCase)))
                cameras.Add(new CameraDevice(match.Groups["name"].Value, id));
        }
        return cameras;
    }

    internal static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    [GeneratedRegex("""
        \[dshow [^\r\n]+\] "(?<name>.+)" \((?<kind>video|audio|none)\)\r?\n\[dshow [^\r\n]+\]\s+Alternative name "(?<id>.+)"
        """)]
    private static partial Regex CameraLine();
}

internal sealed class CameraCapture : IDisposable
{
    private const int Width = 640;
    private const int Height = 360;
    private readonly Process process;
    private readonly TaskCompletionSource<BitmapSource> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BitmapSource? latest;

    internal CameraCapture(string executable, CameraDevice device)
        : this(executable, ["-f", "dshow", "-rtbufsize", "32M", "-i", $"video={device.Id}"]) { }

    internal CameraCapture(string executable, string[] input)
    {
        process = FfmpegMedia.Start(executable,
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-filter_threads", "1", .. input,
                "-an", "-vf", "scale=640:360:force_original_aspect_ratio=decrease,pad=640:360:(ow-iw)/2:(oh-ih)/2,fps=15",
                "-pix_fmt", "bgr24", "-threads", "1", "-f", "rawvideo", "pipe:1"], output: true);
        Completion = Task.Run(ReadFramesAsync);
    }

    internal BitmapSource? Latest => Volatile.Read(ref latest);
    internal Task<BitmapSource> FirstFrame => first.Task;
    internal Task Completion { get; }

    private async Task ReadFramesAsync()
    {
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            var bytes = new byte[Width * Height * 3];
            var stream = process.StandardOutput.BaseStream;
            while (true)
            {
                await stream.ReadExactlyAsync(bytes).ConfigureAwait(false);
                var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgr24, null, bytes, Width * 3);
                bitmap.Freeze();
                Volatile.Write(ref latest, bitmap);
                first.TrySetResult(bitmap);
            }
        }
        catch (IOException)
        {
            FfmpegMedia.Stop(process);
            var detail = (await errors.ConfigureAwait(false)).Trim();
            first.TrySetException(new IOException(detail.Length == 0
                ? "The camera ended before it returned a frame." : detail));
        }
        finally
        {
            Volatile.Write(ref latest, null);
            process.Dispose();
        }
    }

    public void Dispose() => FfmpegMedia.Stop(process);
}
