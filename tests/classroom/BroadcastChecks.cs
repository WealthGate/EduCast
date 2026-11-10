using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EduCast.Studio;

internal static partial class Checks
{
    private static void BroadcastInputs()
    {
        Assert(StreamDestination.Create("Local", "rtmp://127.0.0.1:1935/live", "key") is not null);
        Assert(StreamDestination.Create("YouTube", "rtmps://ingest.example.org/live", "key") is not null);
        Assert(StreamDestination.Create("Twitch", "rtmp://sfo.contribute.live-video.net/app", "key?bandwidthtest=true") is not null);
        foreach (var url in new[] { "file:///c:/live", "http://x/live", "rtmp://example.org/live", "rtmps://x/",
            "rtmps://u:p@example.org/live", "rtmps://example.org/live?secret=key", "rtmps://example.org/live#key",
            "rtmp://sfo.contribute.live-video.net.example.org/app" })
            Assert(StreamDestination.Create("Invalid", url, "key") is null, url);
        Assert(StreamDestination.Create("Bad", "rtmps://example.org/live", "key\nx") is null);

        var buffer = new PcmBuffer();
        var pcm = Enumerable.Repeat((byte)0x7f, StreamFrames.AudioBytes).ToArray();
        for (var i = 0; i < 7; i++)
            buffer.Write(pcm);
        var read = new byte[StreamFrames.AudioBytes * 6];
        buffer.Read(read);
        Assert(read.Take(StreamFrames.AudioBytes * 5).All(b => b == 0x7f));
        Assert(read.Skip(StreamFrames.AudioBytes * 5).All(b => b == 0));
        buffer.Write(pcm);
        buffer.Clear();
        buffer.Read(read);
        Assert(read.All(b => b == 0));
        byte[] samples = [0x30, 0x75, 0xd0, 0x8a];
        PcmBuffer.Mix(samples, samples.ToArray());
        Assert(samples.SequenceEqual(new byte[] { 0xff, 0x7f, 0x00, 0x80 }));
    }

    private static void Streaming(string executable) => InTemporaryDirectory(directory =>
    {
        var engine = Path.GetFullPath(executable);
        var builtIn = Path.GetFileName(engine).Equals("EduCast.Media.exe", StringComparison.OrdinalIgnoreCase);
        var clip = Path.Combine(directory, "teaching-clip.mp4");
        RunEngine(engine, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
            "color=c=blue:s=320x240:r=25,drawbox=x=0:y=0:w=320:h=120:color=red:t=fill",
            "-f", "lavfi", "-i", "sine=f=440:r=48000", "-t", "8", "-c:v", "h264_mf", "-pix_fmt", "nv12",
            "-b:v", "800k", "-c:a", "aac", clip]);
        var wav = Path.Combine(directory, "narration.wav");
        // The fixture uses the .NET WAV writer so the test only needs the shipped decoders and encoders.
        using (var wave = new BinaryWriter(File.Create(wav)))
        {
            const int samples = 48000 * 2;
            wave.Write("RIFF"u8);
            wave.Write(36 + samples * 2);
            wave.Write("WAVEfmt "u8);
            wave.Write(16);
            wave.Write((short)1);
            wave.Write((short)1);
            wave.Write(48000);
            wave.Write(96000);
            wave.Write((short)2);
            wave.Write((short)16);
            wave.Write("data"u8);
            wave.Write(samples * 2);
            for (var i = 0; i < samples; i++)
                wave.Write((short)(8000 * Math.Sin(i * Math.Tau * 320 / 48000)));
        }
        using (var narration = new MediaPlayback(engine, wav, TimeSpan.Zero))
        {
            WaitCondition(() => narration.Started || narration.Completion.IsCompleted, TimeSpan.FromSeconds(6), "audio-only clip");
            var samples = new byte[StreamFrames.AudioBytes];
            narration.Audio.Read(samples);
            Assert(narration.Started && samples.Any(b => b != 0), "audio-only playback did not decode samples");
        }

        using var media = new MediaPlayback(engine, clip, TimeSpan.Zero);
        WaitCondition(() => media.Image is not null || media.Completion.IsCompleted, TimeSpan.FromSeconds(6), "media preview");
        Assert(media.Image is { PixelWidth: 960, PixelHeight: 540 }, "the embedded decoder did not show the clip");
        var top = Pixel(media.Image!, 480, 70);
        var bottom = Pixel(media.Image!, 480, 470);
        Assert(top[2] > 220 && bottom[0] > 220, "clip orientation or colors are wrong");

        var first = Path.Combine(directory, "first.flv");
        var second = Path.Combine(directory, "second.flv");
        var firstUrl = $"rtmp://127.0.0.1:{FreePort()}/live";
        var secondUrl = $"rtmp://127.0.0.1:{FreePort()}/live";
        using var rejecting = new TcpListener(IPAddress.Loopback, 0);
        rejecting.Start();
        var offlineUrl = $"rtmp://127.0.0.1:{((IPEndPoint)rejecting.LocalEndpoint).Port}/live";
        using var rejectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var rejected = Task.Run(async () =>
        {
            using var client = await rejecting.AcceptTcpClientAsync(rejectionTimeout.Token);
        });
        using var firstReceiver = Receive(engine, firstUrl, "first-key", first, "2.5");
        using var secondReceiver = Receive(engine, secondUrl, "second-key", second, "5");
        var firstErrors = firstReceiver.StandardError.ReadToEndAsync();
        var secondErrors = secondReceiver.StandardError.ReadToEndAsync();
        Thread.Sleep(500);
        var destinations = new[]
        {
            StreamDestination.Create("First", firstUrl, "first-key")!,
            StreamDestination.Create("Second", secondUrl, "second-key")!,
            StreamDestination.Create("Offline", offlineUrl, "offline-key")!
        };
        var pixels = new byte[1280 * 720 * 4];
        for (var y = 0; y < 720; y++)
            for (var x = 0; x < 1280; x++)
            {
                var offset = (y * 1280 + x) * 4;
                pixels[offset + (y < 360 ? 2 : 0)] = 245;
                pixels[offset + 3] = 255;
            }

        var job = new ProgramBroadcast(engine, destinations, null, (pcm, _) =>
        {
            media.Audio.Read(pcm);
            return Task.FromResult((pixels, true));
        }, ["-re", "-f", "lavfi", "-i", "sine=f=1000:r=48000"], testEncoder: !builtIn);
        try
        {
            WaitCondition(() => job.Updates.Take(2).All(state => state.Status == StreamStatus.Live) || job.Completion.IsCompleted,
                TimeSpan.FromSeconds(15), "two local destinations to go live");
            if (job.Completion.IsFaulted)
                job.Completion.GetAwaiter().GetResult();
            Assert(job.Updates.Take(2).All(state => state.Status == StreamStatus.Live),
                string.Join(" / ", job.Updates.Select(state => state.Detail)));
            rejected.GetAwaiter().GetResult();
            WaitCondition(() => job.Updates[2].Status == StreamStatus.Failed, TimeSpan.FromSeconds(8),
                "the rejected destination to report its failure");
            WaitCondition(() => firstReceiver.HasExited, TimeSpan.FromSeconds(12), "first receiver to finish");
            WaitCondition(() => job.Updates[0].Status == StreamStatus.Failed, TimeSpan.FromSeconds(10), "first destination to disconnect");
            Assert(job.Updates[1].Status == StreamStatus.Live, "one failed destination stopped a healthy stream");
            WaitCondition(() => secondReceiver.HasExited, TimeSpan.FromSeconds(12), "second receiver to finish");
            Assert(firstReceiver.ExitCode == 0 && secondReceiver.ExitCode == 0, "a local receiver failed");
            foreach (var errors in new[] { firstErrors, secondErrors })
                Assert(!errors.GetAwaiter().GetResult().Contains("Unexpected stream", StringComparison.OrdinalIgnoreCase),
                    "the receiver did not receive the supplied key");
            VerifyReceivedMedia(engine, first);
            VerifyReceivedMedia(engine, second);
        }
        finally
        {
            job.Stop();
            try { job.Completion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
            finally { FfmpegMedia.Stop(firstReceiver); FfmpegMedia.Stop(secondReceiver); }
        }
        if (builtIn)
            RejectUntrustedIngest(engine, pixels);
    });

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Process Receive(string engine, string address, string key, string file, string duration) =>
        FfmpegMedia.Start(engine, ["-hide_banner", "-loglevel", "warning", "-nostdin", "-listen", "1", "-timeout", "20",
            "-i", $"{address}/{key}", "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-t", duration, "-f", "flv", file]);

    private static void VerifyReceivedMedia(string engine, string file)
    {
        var top = RunEngine(engine, ["-hide_banner", "-loglevel", "error", "-i", file, "-map", "0:v:0",
            "-vf", "crop=2:2:640:80", "-frames:v", "1", "-c:v", "rawvideo", "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1"]);
        Assert(top.Length == 12 && top[2] > 210 && top[0] < 20, "the receiver got the wrong video");
        var audio = RunEngine(engine, ["-hide_banner", "-loglevel", "error", "-i", file, "-map", "0:a:0",
            "-t", "1.5", "-f", "s16le", "pipe:1"]);
        Assert(audio.Length >= 34000 * StreamFrames.SampleBytes && audio.Any(b => b != 0), "the receiver got no audible AAC samples");
        Assert(ToneAmplitude(audio, 440) > 250 && ToneAmplitude(audio, 1000) > 250,
            "the received AAC must contain both media audio and the microphone tone");
    }

    private static double ToneAmplitude(byte[] audio, int frequency)
    {
        var sin = 0d;
        var cos = 0d;
        const int samples = 24000;
        for (var i = 0; i < samples; i++)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(audio.AsSpan((i + 10000) * StreamFrames.SampleBytes));
            var angle = i * frequency * Math.Tau / StreamFrames.SampleRate;
            sin += value * Math.Sin(angle);
            cos += value * Math.Cos(angle);
        }
        return Math.Sqrt(sin * sin + cos * cos) * 2 / samples;
    }

    private static void RejectUntrustedIngest(string engine, byte[] pixels)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        var attempt = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            using var tls = new SslStream(connection.GetStream());
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate
                }, timeout.Token);
                return await tls.ReadAsync(new byte[1], timeout.Token) > 0;
            }
            catch (Exception error) when (error is AuthenticationException or IOException) { return false; }
        });
        var target = StreamDestination.Create("Untrusted certificate", $"rtmps://localhost:{port}/live", "test-key")!;
        var job = new ProgramBroadcast(engine, [target], null, (_, _) => Task.FromResult((pixels, true)));
        try
        {
            Assert(!attempt.GetAwaiter().GetResult(), "RTMPS sent stream data to an untrusted server");
            job.Completion.WaitAsync(timeout.Token).GetAwaiter().GetResult();
            Assert(job.Updates[0].Status == StreamStatus.Failed);
        }
        finally
        {
            job.Stop();
            job.Completion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
    }

    private static byte[] RunEngine(string engine, string[] args)
    {
        using var process = FfmpegMedia.Start(engine, args, output: true);
        try
        {
            var errors = process.StandardError.ReadToEndAsync();
            using var output = new MemoryStream();
            var reader = process.StandardOutput.BaseStream.CopyToAsync(output);
            Assert(process.WaitForExit(20000), "media engine command did not finish");
            reader.GetAwaiter().GetResult();
            Assert(process.ExitCode == 0, errors.GetAwaiter().GetResult());
            return output.ToArray();
        }
        finally { FfmpegMedia.Stop(process); }
    }

    private static void WaitCondition(Func<bool> condition, TimeSpan timeout, string description)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > timeout)
                throw new InvalidOperationException($"Timed out waiting for {description}.");
            Thread.Sleep(30);
        }
    }
}
