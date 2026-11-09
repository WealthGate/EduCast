using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace EduCast.Studio;

internal sealed record AvailableUpdate(Version Version, Uri Installer);

internal static class ReleaseCatalog
{
    private const string Releases = "https://github.com/WealthGate/EduCast/releases";
    private static readonly Uri Latest = new("https://api.github.com/repos/WealthGate/EduCast/releases/latest");
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(8),
        MaxResponseContentBufferSize = 128 * 1024
    };

    public static Version InstalledVersion { get; } =
        new(typeof(ReleaseCatalog).Assembly.GetName().Version!.ToString(3));

    public static async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
        request.Headers.UserAgent.ParseAdd("EduCastStudio");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return ReadUpdate(document.RootElement, InstalledVersion);
    }

    internal static AvailableUpdate? ReadUpdate(JsonElement release, Version installed)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
            !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
            !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var name = tag.GetString()!;
        if (!name.StartsWith('v') || !Version.TryParse(name.AsSpan(1), out var version) ||
            version.Build < 0 || version.Revision >= 0 || name != $"v{version}" || version <= installed)
            return null;

        var filename = $"EduCast-Studio-{version}-win-x64.msi";
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind == JsonValueKind.Object &&
                asset.TryGetProperty("name", out var assetName) && assetName.ValueKind == JsonValueKind.String &&
                assetName.GetString() == filename &&
                asset.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String &&
                state.GetString() == "uploaded")
                return new AvailableUpdate(version, new Uri($"{Releases}/download/{name}/{filename}"));
        }

        return null;
    }
}
