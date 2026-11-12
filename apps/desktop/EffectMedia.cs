using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EduCast.Classroom;

namespace EduCast.Studio;

internal sealed class EffectMedia
{
    private sealed record Frame(BitmapSource Bitmap, int End);
    private static readonly ConditionalWeakTable<string, EffectMedia> Cache = new();
    private static readonly HttpClient Downloads = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private readonly List<Frame> frames = [];

    internal bool Animated => frames.Count > 1;
    internal BitmapSource? Preview => frames.Count == 0 ? null : frames[0].Bitmap;

    internal static bool IsGif(string data) => data.StartsWith("R0lGOD", StringComparison.Ordinal);

    internal static EffectMedia For(string data) => Cache.GetValue(data, value =>
    {
        try { return Decode(Convert.FromBase64String(value)); }
        catch (Exception error) when (error is FormatException or IOException or NotSupportedException or ArgumentException or
            InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return new EffectMedia();
        }
    });

    internal BitmapSource? At(double seconds)
    {
        if (!Animated)
            return Preview;
        var position = seconds * 1000 % frames[^1].End;
        return frames.First(frame => frame.End > position).Bitmap;
    }

    internal static async Task<string> FromFile(string path, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        return await Read(input, input.Length, cancellationToken);
    }

    internal static Task<string> Download(string address, CancellationToken cancellationToken) =>
        Download(Downloads, address, cancellationToken);

    internal static async Task<string> Download(HttpClient client, string address, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || !ValidUri(uri))
            throw new InvalidDataException("Enter a direct HTTPS link to a PNG, JPG or GIF.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        for (var redirects = 0; redirects <= 4; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("EduCastStudio");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (response.Headers.Location is not { } target || !Uri.TryCreate(uri, target, out uri) || !ValidUri(uri))
                    throw new InvalidDataException("The image link redirected outside HTTPS.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await Read(input, response.Content.Headers.ContentLength, timeout.Token);
        }
        throw new InvalidDataException("The image link redirected too many times.");
    }

    private static bool ValidUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Length > 0 && uri.UserInfo.Length == 0;

    private static async Task<string> Read(Stream input, long? length, CancellationToken cancellationToken)
    {
        if (length > BoardEffect.MaxImageBytes)
            throw new InvalidDataException("Choose an image smaller than 3 MB.");
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + count > BoardEffect.MaxImageBytes)
                throw new InvalidDataException("Choose an image smaller than 3 MB.");
            output.Write(buffer, 0, count);
        }
        var data = Convert.ToBase64String(output.GetBuffer(), 0, (int)output.Length);
        if (For(data).Preview is null)
            throw new InvalidDataException("Choose a readable PNG, JPG or GIF. Very large images or animations may need to be resized.");
        return data;
    }

    private static EffectMedia Decode(byte[] bytes)
    {
        var media = new EffectMedia();
        var gif = bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8);
        if (!gif && !bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            !bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }))
            return media;
        if (gif && bytes.Length < 13)
            return media;

        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        var first = decoder.Frames[0];
        var width = gif ? bytes[6] | bytes[7] << 8 : first.PixelWidth;
        var height = gif ? bytes[8] | bytes[9] << 8 : first.PixelHeight;
        if (width is < 1 or > 4096 || height is < 1 or > 4096 ||
            decoder.Frames.Count is < 1 or > 160 || (long)width * height * decoder.Frames.Count > 20_000_000)
            return media;

        if (!gif)
        {
            var bitmap = new FormatConvertedBitmap(first, PixelFormats.Pbgra32, null, 0);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            media.frames.Add(new Frame(Bitmap(width, height, pixels), 100));
            return media;
        }

        var canvas = new byte[width * height * 4];
        var background = Background(bytes, decoder);
        if (background != 0)
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(canvas.AsSpan()).Fill(background);
        var end = 0;
        foreach (var source in decoder.Frames)
        {
            var metadata = source.Metadata as BitmapMetadata;
            var left = Query(metadata, "/imgdesc/Left");
            var top = Query(metadata, "/imgdesc/Top");
            var frameWidth = source.PixelWidth;
            var frameHeight = source.PixelHeight;
            if (left + frameWidth > width || top + frameHeight > height || frameWidth < 1 || frameHeight < 1)
                return new EffectMedia();
            var disposal = Query(metadata, "/grctlext/Disposal");
            var previous = disposal == 3 ? (byte[])canvas.Clone() : null;
            var bitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            var pixels = new byte[frameWidth * frameHeight * 4];
            bitmap.CopyPixels(pixels, frameWidth * 4, 0);
            for (var row = 0; row < frameHeight; row++)
            {
                var start = ((top + row) * width + left) * 4;
                var input = row * frameWidth * 4;
                for (var column = 0; column < frameWidth; column++, start += 4, input += 4)
                {
                    if (pixels[input + 3] != 0)
                        pixels.AsSpan(input, 4).CopyTo(canvas.AsSpan(start, 4));
                }
            }
            var delay = Query(metadata, "/grctlext/Delay");
            end += delay == 0 ? 100 : Math.Clamp(delay * 10, 20, 10_000);
            media.frames.Add(new Frame(Bitmap(width, height, canvas), end));
            if (previous is not null)
                canvas = previous;
            else if (disposal == 2)
            {
                for (var row = 0; row < frameHeight; row++)
                    System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(
                        canvas.AsSpan(((top + row) * width + left) * 4, frameWidth * 4)).Fill(background);
            }
        }
        return media;
    }

    private static uint Background(byte[] bytes, BitmapDecoder decoder)
    {
        var globalColors = (bytes[10] & 0x80) == 0 ? 0 : 2 << (bytes[10] & 7);
        var index = bytes[11];
        var palette = 13 + index * 3;
        if (index >= globalColors || palette + 2 >= bytes.Length || decoder.Frames.Any(frame =>
            (frame.Metadata as BitmapMetadata)?.GetQuery("/grctlext/TransparencyFlag") is true))
            return 0;
        return 0xff000000 | (uint)bytes[palette] << 16 | (uint)bytes[palette + 1] << 8 | bytes[palette + 2];
    }

    private static int Query(BitmapMetadata? metadata, string path) => metadata?.GetQuery(path) switch
    {
        byte value => value,
        ushort value => value,
        _ => 0
    };

    private static BitmapSource Bitmap(int width, int height, byte[] pixels)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }
}
