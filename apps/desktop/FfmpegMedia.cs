using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EduCast.Studio;

internal sealed record CameraDevice(string Name, string Id);
internal sealed record MicrophoneDevice(string Name, string Id);
internal sealed record CaptureDevices(IReadOnlyList<CameraDevice> Cameras, IReadOnlyList<MicrophoneDevice> Microphones);

internal static partial class FfmpegMedia
{
    internal static string? Find()
    {
        if (Bundled() is { } bundled)
            return bundled;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = directory.Trim().Trim('"');
            if (Path.IsPathFullyQualified(path) && File.Exists(Path.Combine(path, "ffmpeg.exe")))
                return Path.Combine(path, "ffmpeg.exe");
        }
        return null;
    }

    internal static string? Bundled()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "EduCast.Media.exe");
        return File.Exists(path) ? path : null;
    }

    internal static Process Start(string executable, IEnumerable<string> args, bool input = false, bool output = false,
        string? streamKey = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            RedirectStandardInput = input, RedirectStandardOutput = output
        };
        foreach (var argument in args)
            info.ArgumentList.Add(argument);
        info.Environment.Remove("FFREPORT");
        info.Environment.Remove("EDUCAST_STREAM_KEY");
        if (streamKey is not null)
            info.Environment["EDUCAST_STREAM_KEY"] = streamKey;
        return Process.Start(info) ?? throw new IOException("Could not start FFmpeg.");
    }

    internal static async Task<CaptureDevices> DevicesAsync(string executable)
    {
        using var process = Start(executable, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"]);
        var output = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return ParseDevices(await output.ConfigureAwait(false));
        }
        finally { Stop(process); }
    }

    internal static CaptureDevices ParseDevices(string listing)
    {
        var cameras = new List<CameraDevice>();
        var microphones = new List<MicrophoneDevice>();
        foreach (Match match in DeviceLine().Matches(listing))
        {
            var id = match.Groups["id"].Value;
            var name = match.Groups["name"].Value;
            var kind = match.Groups["kind"].Value;
            if (kind == "video" || (kind == "none" && id.Contains("{65e8773d-", StringComparison.OrdinalIgnoreCase)))
                cameras.Add(new CameraDevice(name, id));
            else if (kind == "audio")
                microphones.Add(new MicrophoneDevice(name, id));
        }
        return new CaptureDevices(cameras, microphones);
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
        \[(?<prefix>(?:dshow|in\#\d+) [^\r\n]+)\] "(?<name>.+)" \((?<kind>video|audio|none)\)\r?\n\[\k<prefix>\]\s+Alternative name "(?<id>.+)"
        """)]
    private static partial Regex DeviceLine();
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
