using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EduCast.Studio;

internal sealed record RecordingProfile(string Label, int Width, int Height)
{
    internal const int Fps = 15;
    internal static readonly RecordingProfile[] Options =
    [
        new("720p · 15 fps", 1280, 720), new("1080p · 15 fps", 1920, 1080),
        new("4K UHD · 15 fps (software)", 3840, 2160)
    ];
}

internal sealed class ProgramVideoRenderer(RecordingProfile profile)
{
    private readonly ProgramSurface surface = new();
    private readonly byte[] pixels = new byte[profile.Width * profile.Height * 4];
    private StudioFrame? previous;

    internal byte[] Capture(StudioFrame frame)
    {
        if (frame == previous)
            return pixels;
        surface.Present(frame.Board);
        surface.PresentInk(frame.LiveInk);
        surface.PresentLayers(frame.Layers);
        var size = new Size(BoardSurface.BoardWidth, BoardSurface.BoardHeight);
        surface.Measure(size);
        surface.Arrange(new Rect(size));
        surface.UpdateLayout();

        var dpi = 96 * profile.Width / BoardSurface.BoardWidth;
        var image = new RenderTargetBitmap(profile.Width, profile.Height, dpi, dpi, PixelFormats.Pbgra32);
        image.Render(surface);
        image.CopyPixels(pixels, profile.Width * 4, 0);
        previous = frame;
        return pixels;
    }
}

internal sealed class ProgramRecording
{
    private readonly PeriodicTimer ticks;
    private readonly Stopwatch time = Stopwatch.StartNew();

    internal ProgramRecording(string ffmpeg, string destination, RecordingProfile profile, Func<Task<byte[]>> capture)
    {
        var partial = Path.Combine(Path.GetDirectoryName(destination)!,
            $".{Path.GetFileNameWithoutExtension(destination)}-{Guid.NewGuid():N}.partial.mp4");
        var process = FfmpegMedia.Start(ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "rawvideo", "-pixel_format", "bgra",
                "-video_size", $"{profile.Width}x{profile.Height}", "-framerate", RecordingProfile.Fps.ToString(), "-i", "pipe:0",
                "-an", "-c:v", "libx264", "-threads", "4", "-preset", "ultrafast", "-crf", "20", "-pix_fmt", "yuv420p",
                "-g", "30", "-movflags", "+frag_keyframe+empty_moov+default_base_moof", "-f", "mp4", partial], input: true);
        ticks = new PeriodicTimer(TimeSpan.FromSeconds(1d / RecordingProfile.Fps));
        Completion = Task.Run(() => RecordAsync(process, partial, destination, profile, capture));
    }

    internal TimeSpan Elapsed => time.Elapsed;
    internal Task<string> Completion { get; }
    internal void Stop() => ticks.Dispose();

    private async Task<string> RecordAsync(Process process, string partial, string destination,
        RecordingProfile profile, Func<Task<byte[]>> capture)
    {
        var errors = process.StandardError.ReadToEndAsync();
        var input = process.StandardInput.BaseStream;
        try
        {
            long framesWritten = 0;
            do
            {
                var frame = await capture().ConfigureAwait(false);
                if (frame.Length != profile.Width * profile.Height * 4)
                    throw new InvalidDataException("The encoder received an invalid frame size.");
                var due = (long)(time.Elapsed.TotalSeconds * RecordingProfile.Fps) + 1;
                var repeats = Math.Max(1, due - framesWritten);
                if (repeats > RecordingProfile.Fps * 2)
                    throw new IOException("The encoder fell behind. Try a lower recording resolution.");
                using var writeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                for (var i = 0L; i < repeats; i++)
                {
                    await input.WriteAsync(frame, writeTimeout.Token).ConfigureAwait(false);
                    framesWritten++;
                }
            }
            while (await ticks.WaitForNextTickAsync().ConfigureAwait(false));

            input.Close();
            using var finalizeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await process.WaitForExitAsync(finalizeTimeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IOException("FFmpeg exited with an error.");
            File.Move(partial, destination, overwrite: true);
            return destination;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            FfmpegMedia.Stop(process);
            var detail = (await errors.ConfigureAwait(false)).Trim();
            var recovery = File.Exists(partial) ? $" Partial recording: {partial}" : "";
            throw new IOException($"Recording could not be completed. {error.Message} {detail}{recovery}".Trim(), error);
        }
        finally
        {
            time.Stop();
            ticks.Dispose();
            FfmpegMedia.Stop(process);
            process.Dispose();
        }
    }
}
