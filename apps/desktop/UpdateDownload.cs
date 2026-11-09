using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace EduCast.Studio;

internal static class UpdateDownload
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    internal static async Task SaveAsync(AvailableUpdate update, string destination, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await SaveAsync(Client, update, destination, timeout.Token).ConfigureAwait(false);
    }

    internal static async Task SaveAsync(HttpClient client, AvailableUpdate update, string destination,
        CancellationToken cancellationToken)
    {
        if (update.Size is <= 0 or > ReleaseCatalog.MaxInstallerBytes || !ReleaseCatalog.IsSha256(update.Sha256))
            throw new InvalidDataException("The release does not have a valid installer size and checksum.");

        var uri = update.Installer;
        for (var redirects = 0; redirects <= 4; redirects++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("The installer download was redirected away from GitHub.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("EduCastStudio");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                uri = response.Headers.Location is { } location
                    ? new Uri(uri, location)
                    : throw new InvalidDataException("GitHub returned an invalid installer redirect.");
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != update.Size)
                throw new InvalidDataException("The installer size does not match the GitHub release.");

            var created = false;
            try
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous);
                created = true;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[65536];
                long written = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += count;
                    if (written > update.Size)
                        throw new InvalidDataException("The downloaded installer exceeds the published size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
                if (written != update.Size ||
                    !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(update.Sha256)))
                    throw new InvalidDataException("The downloaded installer failed its size or checksum check.");
                return;
            }
            catch
            {
                if (created)
                    TryDelete(destination);
                throw;
            }
        }

        throw new InvalidDataException("GitHub returned too many installer redirects.");
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
