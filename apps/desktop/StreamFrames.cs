using System.Buffers.Binary;
using System.IO;

namespace EduCast.Studio;

// Streaming AVI carries uncompressed video and PCM together over one private pipe.
internal static class StreamFrames
{
    internal const int Fps = 25;
    internal const int SampleRate = 48000;
    internal const int SampleBytes = 4; // Signed 16-bit, stereo.
    internal const int AudioBytes = SampleRate / Fps * SampleBytes;

    internal static byte[] Header(int width, int height)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var frameBytes = width * height * 4;
        void Ints(params int[] values)
        {
            foreach (var value in values)
                writer.Write(value);
        }

        void List(string kind, Action write) => Sized("LIST", () => { writer.Write(FourCc(kind)); write(); });

        void Sized(string id, Action write)
        {
            writer.Write(FourCc(id));
            var length = stream.Position;
            writer.Write(0);
            write();
            var end = stream.Position;
            stream.Position = length;
            writer.Write((int)(end - length - 4));
            stream.Position = end;
        }

        writer.Write("RIFF"u8);
        writer.Write(-1);
        writer.Write("AVI "u8);
        List("hdrl", () =>
        {
            Sized("avih", () => Ints(1_000_000 / Fps, Fps * frameBytes + SampleRate * SampleBytes,
                0, 0x100, 0, 0, 2, frameBytes, width, height, 0, 0, 0, 0));
            List("strl", () =>
            {
                Sized("strh", () =>
                {
                    writer.Write("vidsDIB "u8);
                    Ints(0, 0, 0, 1, Fps, 0, 0, frameBytes, -1, 0, 0, height << 16 | width);
                });
                Sized("strf", () =>
                {
                    Ints(40, width, -height);
                    writer.Write((short)1);
                    writer.Write((short)32);
                    Ints(0, frameBytes, 0, 0, 0, 0);
                });
            });
            List("strl", () =>
            {
                Sized("strh", () =>
                {
                    writer.Write("auds"u8);
                    Ints(0, 0, 0, 0, SampleBytes, SampleRate * SampleBytes, 0, 0, AudioBytes, -1, SampleBytes, 0, 0);
                });
                Sized("strf", () =>
                {
                    writer.Write((short)1);
                    writer.Write((short)2);
                    Ints(SampleRate, SampleRate * SampleBytes);
                    writer.Write((short)SampleBytes);
                    writer.Write((short)16);
                });
            });
        });
        writer.Write("LIST"u8);
        writer.Write(-1);
        writer.Write("movi"u8);
        return stream.ToArray();
    }

    private static uint FourCc(string text) => (uint)(text[0] | text[1] << 8 | text[2] << 16 | text[3] << 24);

    internal static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> video, ReadOnlyMemory<byte> audio,
        byte[] header, CancellationToken cancel)
    {
        "00db"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), video.Length);
        await stream.WriteAsync(header, cancel).ConfigureAwait(false);
        await stream.WriteAsync(video, cancel).ConfigureAwait(false);
        "01wb"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), audio.Length);
        await stream.WriteAsync(header, cancel).ConfigureAwait(false);
        await stream.WriteAsync(audio, cancel).ConfigureAwait(false);
    }
}

internal sealed class PcmBuffer
{
    private readonly byte[] buffer = new byte[StreamFrames.AudioBytes * 5];
    private int count;
    private int start;
    private bool playing;

    internal void Write(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length > buffer.Length)
            pcm = pcm[^buffer.Length..];
        lock (buffer)
        {
            var discard = Math.Max(0, count + pcm.Length - buffer.Length);
            start = (start + discard) % buffer.Length;
            count -= discard;
            var end = (start + count) % buffer.Length;
            var first = Math.Min(buffer.Length - end, pcm.Length);
            pcm[..first].CopyTo(buffer.AsSpan(end));
            pcm[first..].CopyTo(buffer);
            count += pcm.Length;
        }
    }

    internal void Read(Span<byte> pcm, bool paced = false)
    {
        pcm.Clear();
        lock (buffer)
        {
            if (paced)
            {
                playing = count >= pcm.Length * (playing ? 1 : 2);
                if (!playing)
                    return;
            }
            var length = Math.Min(pcm.Length, count);
            var first = Math.Min(buffer.Length - start, length);
            buffer.AsSpan(start, first).CopyTo(pcm);
            buffer.AsSpan(0, length - first).CopyTo(pcm[first..]);
            start = (start + length) % buffer.Length;
            count -= length;
        }
    }

    internal void Clear()
    {
        lock (buffer)
        {
            count = 0;
            start = 0;
            playing = false;
        }
    }

    internal static void Mix(Span<byte> target, ReadOnlySpan<byte> other)
    {
        for (var i = 0; i < target.Length; i += 2)
        {
            var sum = BinaryPrimitives.ReadInt16LittleEndian(target[i..]) +
                BinaryPrimitives.ReadInt16LittleEndian(other[i..]);
            BinaryPrimitives.WriteInt16LittleEndian(target[i..], (short)Math.Clamp(sum, short.MinValue, short.MaxValue));
        }
    }
}
