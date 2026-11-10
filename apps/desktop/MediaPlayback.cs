using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EduCast.Studio;

internal sealed class MediaPlayback : IDisposable
{
    internal const string Filter = "Video or audio (*.mp4;*.mov;*.m4v;*.webm;*.mp3;*.m4a;*.wav;*.aac)|" +
        "*.mp4;*.mov;*.m4v;*.webm;*.mp3;*.m4a;*.wav;*.aac";
    private const int Width = 960;
    private const int Height = 540;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Stopwatch clock = new();
    private BitmapSource? image;
    private int stopped;

    internal MediaPlayback(string executable, string file, TimeSpan start)
    {
        var format = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".mp4" or ".mov" or ".m4v" or ".m4a" => "mov",
            ".webm" => "matroska",
            ".mp3" => "mp3",
            ".wav" => "wav",
            ".aac" => "aac",
            _ => throw new IOException("Choose a supported local video or audio file.")
        };
        Start = start;
        var offset = start > TimeSpan.Zero ? new[] { "-ss", start.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) } : [];
        var process = FfmpegMedia.Start(executable, ["-hide_banner", "-loglevel", "error", "-nostdin",
            "-protocol_whitelist", "file", "-f", format, .. offset, "-re", "-i", file,
            "-map", "0:V:0?", "-map", "0:a:0?", "-sn", "-dn", "-vf",
            "scale=960:540:force_original_aspect_ratio=decrease,pad=960:540:(ow-iw)/2:(oh-ih)/2,fps=25,setsar=1",
            "-c:v", "rawvideo", "-threads:v", "1", "-pix_fmt", "bgra", "-c:a", "pcm_s16le", "-ar", "48000", "-ac", "2",
            "-max_interleave_delta", "100000", "-flush_packets", "1", "-f", "avi", "pipe:1"], output: true);
        Completion = Task.Run(() => ReadAsync(process, lifetime.Token));
    }

    internal TimeSpan Start { get; }
    internal TimeSpan Position => Start + clock.Elapsed;
    internal bool Started => clock.IsRunning || Position > Start;
    internal BitmapSource? Image => Volatile.Read(ref image);
    internal PcmBuffer Audio { get; } = new();
    internal Task<bool> Completion { get; }

    private async Task<bool> ReadAsync(Process process, CancellationToken cancel)
    {
        using var terminate = cancel.Register(() => FfmpegMedia.Stop(process));
        var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try
        {
            var output = process.StandardOutput.BaseStream;
            var riff = new byte[12];
            var header = new byte[8];
            await output.ReadExactlyAsync(riff, cancel).ConfigureAwait(false);
            if (!riff.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !riff.AsSpan(8, 4).SequenceEqual("AVI "u8))
                return false;
            var buffer = new byte[Width * Height * 4];
            for (var i = 0; ; i++)
            {
                await output.ReadExactlyAsync(header, cancel).ConfigureAwait(false);
                var size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                var list = header.AsSpan(0, 4).SequenceEqual("LIST"u8);
                if (list)
                {
                    await output.ReadExactlyAsync(riff.AsMemory(0, 4), cancel).ConfigureAwait(false);
                    if (riff.AsSpan(0, 4).SequenceEqual("movi"u8))
                        break;
                }
                if (i >= 24 || size < (list ? 4 : 0) || size > buffer.Length - 1)
                    return false;
                await output.ReadExactlyAsync(buffer.AsMemory(0, size + (size & 1) - (list ? 4 : 0)), cancel)
                    .ConfigureAwait(false);
            }

            while (true)
            {
                var read = await output.ReadAsync(header.AsMemory(0, 1), cancel).ConfigureAwait(false);
                if (read == 0)
                    break;
                await output.ReadExactlyAsync(header.AsMemory(1), cancel).ConfigureAwait(false);
                var size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                if (size < 0 || size > buffer.Length || size == buffer.Length && (size & 1) != 0)
                    return false;
                await output.ReadExactlyAsync(buffer.AsMemory(0, size + (size & 1)), cancel).ConfigureAwait(false);
                var type = header.AsSpan(2, 2);
                if (type.SequenceEqual("dc"u8) || type.SequenceEqual("db"u8))
                {
                    if (size != buffer.Length)
                        return false;
                    var frame = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, buffer, Width * 4);
                    frame.Freeze();
                    Volatile.Write(ref image, frame);
                    clock.Start();
                }
                else if (type.SequenceEqual("wb"u8) && size % StreamFrames.SampleBytes == 0)
                {
                    Audio.Write(buffer.AsSpan(0, size));
                    clock.Start();
                }
            }
            await process.WaitForExitAsync(cancel).ConfigureAwait(false);
            return process.ExitCode == 0 && Started;
        }
        catch (Exception error) when (error is IOException or Win32Exception or OperationCanceledException) { return false; }
        finally
        {
            clock.Stop();
            FfmpegMedia.Stop(process);
            await errors.ConfigureAwait(false);
            process.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
            return;
        lifetime.Cancel();
        Audio.Clear();
        _ = Completion.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
    }
}
