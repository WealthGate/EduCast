using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EduCast.Studio;

internal sealed class BrowserLesson : IDisposable
{
    internal const string FileFilter = "Teaching files (*.html;*.pdf;*.svg;*.png;*.jpg;*.gif;*.webp)|" +
        "*.html;*.htm;*.pdf;*.svg;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp";
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".html", ".htm", ".svg", ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
    private string? localHost;
    private ulong navigation;
    private bool disposed;

    public WebView2CompositionControl View { get; } = new();
    public bool Ready { get; private set; }
    public string Address { get; private set; } = "";
    public event Action? Loading;
    public event Action<string>? Loaded;

    public static bool IsSupportedFile(string path) => Extensions.Contains(Path.GetExtension(path));

    public static Uri? WebAddress(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Any(char.IsWhiteSpace) || text.Contains('\\') || text.StartsWith('/'))
            return null;
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) && IsWeb(absolute))
            return absolute;
        if (text.Contains("://", StringComparison.Ordinal))
            return null;
        return Uri.TryCreate($"https://{text}", UriKind.Absolute, out var web) && IsWeb(web) ? web : null;
    }

    public async Task InitializeAsync()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EduCast Studio", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: directory);
        if (disposed)
            return;
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = true;
        await View.EnsureCoreWebView2Async(environment, options);
        if (disposed)
            return;
        var core = View.CoreWebView2;
        core.IsMuted = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
        core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (args.IsUserInitiated && Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && IsWeb(uri))
                core.Navigate(uri.AbsoluteUri);
        };
        core.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || !IsWeb(uri))
            {
                args.Cancel = true;
                return;
            }
            if (localHost is not null && !string.Equals(uri.Host, localHost, StringComparison.OrdinalIgnoreCase))
                ClearLocalFolder();
            navigation = args.NavigationId;
            Ready = false;
            Loading?.Invoke();
        };
        core.NavigationCompleted += (_, args) =>
        {
            if (args.NavigationId != navigation)
                return;
            Ready = args.IsSuccess;
            Address = Uri.TryCreate(core.Source, UriKind.Absolute, out var source) && source.Host == localHost
                ? Uri.UnescapeDataString(source.Segments[^1]) : core.Source;
            Loaded?.Invoke(args.IsSuccess ? "Ready. Interact with the page or choose a drawing tool."
                : $"The page could not be loaded ({args.WebErrorStatus}).");
        };
    }

    public void Open(Uri uri)
    {
        ClearLocalFolder();
        Ready = false;
        View.CoreWebView2.Navigate(uri.AbsoluteUri);
    }

    public void OpenFile(string path)
    {
        if (!IsSupportedFile(path) || !File.Exists(path))
            throw new IOException("Choose an existing HTML, PDF or supported image file.");
        ClearLocalFolder();
        localHost = $"lesson-{Guid.NewGuid():N}.educast.invalid";
        View.CoreWebView2.SetVirtualHostNameToFolderMapping(localHost, Path.GetDirectoryName(path)!,
            CoreWebView2HostResourceAccessKind.Deny);
        Ready = false;
        View.CoreWebView2.Navigate($"https://{localHost}/{Uri.EscapeDataString(Path.GetFileName(path))}");
    }

    public async Task<BitmapSource?> ReadAsync()
    {
        if (!Ready || disposed)
            return null;
        var current = navigation;
        using var stream = new MemoryStream();
        try { await View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream); }
        catch (COMException) when (!Ready || disposed || current != navigation) { return null; }
        if (!Ready || disposed || current != navigation)
            return null;
        var frame = await Task.Run(() =>
        {
            stream.Position = 0;
            var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            bitmap.Freeze();
            return bitmap;
        });
        return !disposed && Ready && current == navigation ? frame : null;
    }

    private static bool IsWeb(Uri uri) => (uri.Scheme is "http" or "https") && uri.Host.Length > 0 && uri.UserInfo.Length == 0;

    private void ClearLocalFolder()
    {
        if (localHost is null)
            return;
        View.CoreWebView2.ClearVirtualHostNameToFolderMapping(localHost);
        localHost = null;
    }

    public void Dispose()
    {
        disposed = true;
        View.Dispose();
    }
}
