using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;

namespace EduCast.Studio;

internal sealed class StreamDestination
{
    private StreamDestination(string name, Uri server, string key) => (Name, Server, Key) = (name, server, key);

    internal string Name { get; }
    internal Uri Server { get; }
    internal string Key { get; }

    internal static StreamDestination? Create(string name, string address, string key)
    {
        if (address.Length > 1024 || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var server) ||
            (server.Scheme != "rtmps" && !(server.Scheme == "rtmp" && IsPlainRtmpAllowed(server))) ||
            server.Host.Length == 0 || server.AbsolutePath.Trim('/').Length == 0 ||
            server.UserInfo.Length != 0 || server.Fragment.Length != 0 || server.Query.Length != 0 ||
            key.Length is < 1 or > 512 || key.Any(c => c is < '!' or > '~'))
            return null;
        return new StreamDestination(name, server, key);
    }

    private static bool IsPlainRtmpAllowed(Uri server) => server.IsLoopback ||
        server.IdnHost.EndsWith(".contribute.live-video.net", StringComparison.OrdinalIgnoreCase) ||
        server.IdnHost.Equals("live.twitch.tv", StringComparison.OrdinalIgnoreCase);
}

internal enum StreamStatus { Connecting, Live, Failed, Stopped }
internal sealed record StreamUpdate(StreamStatus Status, string Detail);

internal sealed class ProgramBroadcast
{
    internal static readonly RecordingProfile Profile = new("720p · 25 fps", 1280, 720);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Publisher[] publishers;
    private readonly PcmBuffer micPcm = new();
    private int stopping;
    private int micMuted;
    private int micEnded;

    internal ProgramBroadcast(string executable, IReadOnlyList<StreamDestination> destinations, MicrophoneDevice? microphone,
        Func<byte[], CancellationToken, Task<(byte[] Video, bool AllowMic)>> capture, string[]? testAudio = null,
        bool testEncoder = false)
    {
        IncludesMic = microphone is not null || testAudio is not null;
        publishers = destinations.Select(d => new Publisher(executable, d, lifetime.Token, testEncoder)).ToArray();
        var microphoneTask = IncludesMic ? CaptureMicAsync(executable, testAudio ??
            ["-f", "dshow", "-audio_buffer_size", "50", "-i", $"audio={microphone!.Id}"], lifetime.Token) : Task.CompletedTask;
        Completion = Task.Run(async () =>
        {
            try { await EncodeAsync(executable, capture, lifetime.Token).ConfigureAwait(false); }
            finally
            {
                Stop();
                await Task.WhenAll(publishers.Select(p => p.Completion).Append(microphoneTask)).ConfigureAwait(false);
                lifetime.Dispose();
            }
        });
    }

    internal Task Completion { get; }
    internal bool IncludesMic { get; }
    internal bool MicEnded => Volatile.Read(ref micEnded) == 1;
    internal bool MicMuted
    {
        get => Volatile.Read(ref micMuted) == 1;
        set
        {
            Volatile.Write(ref micMuted, value ? 1 : 0);
            micPcm.Clear();
        }
    }
    internal IReadOnlyList<StreamUpdate> Updates => publishers.Select(p => p.Update).ToArray();

    internal void Stop()
    {
        if (Interlocked.Exchange(ref stopping, 1) == 0)
            lifetime.Cancel();
    }

    private async Task CaptureMicAsync(string executable, string[] input, CancellationToken cancel)
    {
        try
        {
            using var mic = FfmpegMedia.Start(executable, ["-hide_banner", "-loglevel", "error", "-nostdin",
                .. input, "-map", "0:a:0", "-vn", "-ac", "2", "-ar", "48000", "-f", "s16le", "pipe:1"], output: true);
            using var terminate = cancel.Register(() => FfmpegMedia.Stop(mic));
            var errors = mic.StandardError.BaseStream.CopyToAsync(Stream.Null, cancel);
            try
            {
                var pcm = new byte[StreamFrames.AudioBytes / 2];
                while (!cancel.IsCancellationRequested)
                {
                    await mic.StandardOutput.BaseStream.ReadExactlyAsync(pcm, cancel).ConfigureAwait(false);
                    if (!MicMuted)
                        micPcm.Write(pcm);
                }
            }
            finally
            {
                FfmpegMedia.Stop(mic);
                await errors.ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or Win32Exception or OperationCanceledException) { }
        finally
        {
            micPcm.Clear();
            Volatile.Write(ref micEnded, 1);
        }
    }

    private async Task EncodeAsync(string executable, Func<byte[], CancellationToken, Task<(byte[] Video, bool AllowMic)>> capture,
        CancellationToken cancel)
    {
        using var encoder = FfmpegMedia.Start(executable, ["-hide_banner", "-loglevel", "error", "-nostdin",
            "-probesize", "32", "-analyzeduration", "0", "-f", "avi", "-i", "pipe:0",
            "-map", "0:v:0", "-map", "0:a:0", "-c:v", "h264_mf", "-rate_control", "cbr", "-scenario", "live_streaming",
            "-pix_fmt", "nv12", "-b:v", "3000k", "-bufsize", "6000k", "-g", "50", "-bf", "0",
            "-c:a", "aac", "-b:a", "128k", "-ar", "48000", "-flvflags", "no_duration_filesize",
            "-flush_packets", "1", "-f", "flv", "pipe:1"], input: true, output: true);
        using var terminate = cancel.Register(() => FfmpegMedia.Stop(encoder));
        var errors = encoder.StandardError.ReadToEndAsync();
        var feed = FeedAsync(encoder, capture, cancel);
        var distribute = DistributeAsync(encoder.StandardOutput.BaseStream, cancel);
        try
        {
            var completed = await Task.WhenAny(feed, distribute).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            if (!cancel.IsCancellationRequested && publishers.Any(p => !p.Completion.IsCompleted))
                throw new IOException("The embedded encoder stopped sending frames.");
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            FfmpegMedia.Stop(encoder);
            if (!cancel.IsCancellationRequested)
            {
                var detail = (await errors.ConfigureAwait(false)).Trim();
                throw new IOException(detail.Length == 0 ? error.Message : $"The encoder could not start or continue. {detail}", error);
            }
        }
        finally
        {
            Stop();
            FfmpegMedia.Stop(encoder);
            await errors.ConfigureAwait(false);
            try { await Task.WhenAll(feed, distribute).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or OperationCanceledException) { }
        }
    }

    private async Task FeedAsync(Process encoder, Func<byte[], CancellationToken, Task<(byte[] Video, bool AllowMic)>> capture,
        CancellationToken cancel)
    {
        var input = encoder.StandardInput.BaseStream;
        await input.WriteAsync(StreamFrames.Header(Profile.Width, Profile.Height), cancel).ConfigureAwait(false);
        var media = new byte[StreamFrames.AudioBytes];
        var mic = new byte[media.Length];
        var silence = new byte[media.Length];
        var header = new byte[8];
        var clock = Stopwatch.StartNew();
        var sent = 0L;
        using var ticks = new PeriodicTimer(TimeSpan.FromSeconds(1d / StreamFrames.Fps));
        do
        {
            Array.Clear(media);
            var (frame, allowMic) = await capture(media, cancel).ConfigureAwait(false);
            if (frame.Length != Profile.Width * Profile.Height * 4)
                throw new IOException("Program supplied an invalid video frame.");
            micPcm.Read(mic);
            if (allowMic && !MicMuted)
                PcmBuffer.Mix(media, mic);
            var due = (long)(clock.Elapsed.TotalSeconds * StreamFrames.Fps) + 1;
            if (due - sent > StreamFrames.Fps * 2)
                throw new IOException("The streaming encoder fell more than two seconds behind.");
            using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            stalled.CancelAfter(TimeSpan.FromSeconds(8));
            using var terminate = stalled.Token.Register(() => FfmpegMedia.Stop(encoder));
            for (; sent < due - 1; sent++)
                await StreamFrames.WriteAsync(input, frame, silence, header, stalled.Token).ConfigureAwait(false);
            await StreamFrames.WriteAsync(input, frame, media, header, stalled.Token).ConfigureAwait(false);
            sent++;
        }
        while (await ticks.WaitForNextTickAsync(cancel).ConfigureAwait(false));
    }

    private async Task DistributeAsync(Stream output, CancellationToken cancel)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var size = await output.ReadAsync(buffer, cancel).ConfigureAwait(false);
            if (size == 0)
                return;
            var payload = buffer.AsSpan(0, size).ToArray();
            var any = false;
            foreach (var publisher in publishers)
                any |= publisher.Enqueue(payload);
            if (!any)
            {
                await Task.WhenAll(publishers.Select(p => p.Completion)).ConfigureAwait(false);
                return;
            }
        }
    }

    private sealed class Publisher
    {
        private readonly Channel<byte[]> queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(160)
        {
            SingleReader = true, SingleWriter = true
        });
        private readonly CancellationTokenSource run;
        private readonly CancellationToken broadcast;
        private StreamUpdate update = new(StreamStatus.Connecting, "Waiting for the destination to accept video.");

        internal Publisher(string exe, StreamDestination destination, CancellationToken broadcast, bool testEncoder)
        {
            this.broadcast = broadcast;
            run = CancellationTokenSource.CreateLinkedTokenSource(broadcast);
            run.CancelAfter(TimeSpan.FromSeconds(20));
            Completion = Task.Run(() => PublishAsync(exe, destination, testEncoder));
        }

        internal Task Completion { get; }
        internal StreamUpdate Update => Volatile.Read(ref update);

        internal bool Enqueue(byte[] bytes)
        {
            if (Update.Status is StreamStatus.Failed or StreamStatus.Stopped)
                return false;
            if (queue.Writer.TryWrite(bytes))
                return true;
            Volatile.Write(ref update, new(StreamStatus.Failed, "Connection could not keep up with the video."));
            try { run.Cancel(); }
            catch (ObjectDisposedException) { }
            return false;
        }

        private async Task PublishAsync(string exe, StreamDestination destination, bool testEncoder)
        {
            var cancel = run.Token;
            try
            {
                var address = destination.Server;
                var app = address.AbsolutePath.Trim('/');
                using var process = FfmpegMedia.Start(exe, ["-hide_banner", "-loglevel", "error", "-nostdin",
                    "-stats_period", "0.5", "-progress", "pipe:1", "-probesize", "65536", "-analyzeduration", "500000",
                    "-f", "flv", "-i", "pipe:0", "-map", "0:v:0", "-map", "0:a:0", "-c", "copy",
                    "-flvflags", "no_duration_filesize", "-rw_timeout", "8000000", "-rtmp_app", app,
                    "-rtmp_playpath", testEncoder ? destination.Key : "__EDUCAST_ENV_KEY__",
                    "-f", "flv", address.AbsoluteUri], input: true, output: true,
                    streamKey: testEncoder ? null : destination.Key);
                using var terminate = cancel.Register(() => FfmpegMedia.Stop(process));
                // Ingest servers can echo a key in errors. Drain those diagnostics without persisting or displaying them.
                var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancel);
                var progress = MonitorAsync(process.StandardOutput, cancel);
                var send = SendAsync(process.StandardInput.BaseStream, cancel);
                var exited = process.WaitForExitAsync(cancel);
                try
                {
                    var ended = await Task.WhenAny(send, exited, progress).ConfigureAwait(false);
                    await ended.ConfigureAwait(false);
                }
                finally
                {
                    FfmpegMedia.Stop(process);
                    queue.Writer.TryComplete();
                    await Task.WhenAll(send, progress, errors, exited).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException or Win32Exception or OperationCanceledException) { }
            finally
            {
                if (broadcast.IsCancellationRequested && Update.Status != StreamStatus.Failed)
                    Volatile.Write(ref update, new(StreamStatus.Stopped, "Stream stopped."));
                else if (Update.Status != StreamStatus.Failed)
                    Volatile.Write(ref update, new(StreamStatus.Failed, Update.Status == StreamStatus.Live
                        ? "The destination disconnected." : "Connection failed. Check the ingest address, stream key and account."));
                queue.Writer.TryComplete();
                run.Dispose();
            }
        }

        private async Task SendAsync(Stream input, CancellationToken cancel)
        {
            await foreach (var bytes in queue.Reader.ReadAllAsync(cancel).ConfigureAwait(false))
                await input.WriteAsync(bytes, cancel).ConfigureAwait(false);
        }

        private async Task MonitorAsync(StreamReader progress, CancellationToken cancel)
        {
            while (await progress.ReadLineAsync(cancel).ConfigureAwait(false) is { } line)
            {
                if (Update.Status is StreamStatus.Failed or StreamStatus.Stopped)
                    return;
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan("out_time_us=".Length), out var time) && time > 0)
                {
                    run.CancelAfter(TimeSpan.FromSeconds(15));
                    Volatile.Write(ref update, new(StreamStatus.Live, "Sending video and audio."));
                }
            }
        }
    }
}
