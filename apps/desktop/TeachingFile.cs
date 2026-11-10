using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EduCast.Studio;

internal enum TeachingFileKind { Board, Browser, Media, DesktopApp }

internal static class TeachingFile
{
    private const string Documents = "*.ppt;*.pptx;*.pps;*.ppsx;*.pptm;*.ppsm;*.odp;*.doc;*.docx;*.docm;*.odt;*.xls;*.xlsx;*.xlsm;*.ods";
    internal const string Filter = "All files (*.*)|*.*|EduCast lesson (*.educast)|*.educast|" +
        BrowserLesson.FileFilter + "|" + MediaPlayback.Filter +
        "|Documents and presentations|" + Documents;
    private static readonly HashSet<string> DocumentExtensions = new(
        Documents.Split(';').Select(pattern => pattern[1..]), StringComparer.OrdinalIgnoreCase);

    internal static TeachingFileKind Classify(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".educast", StringComparison.OrdinalIgnoreCase))
            return TeachingFileKind.Board;
        if (BrowserLesson.IsSupportedFile(path))
            return TeachingFileKind.Browser;
        return MediaPlayback.IsSupportedFile(path) ? TeachingFileKind.Media : TeachingFileKind.DesktopApp;
    }

    internal static string? LocalPath(string text)
    {
        var path = text.Trim().Trim('"');
        if (Path.IsPathFullyQualified(path))
            return path;
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile &&
            Path.IsPathFullyQualified(uri.LocalPath) ? uri.LocalPath : null;
    }

    internal static bool OpenInDesktopApp(nint owner, string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The selected file is no longer available.", path);

        if (DocumentExtensions.Contains(Path.GetExtension(path)))
        {
            try
            {
                using var launched = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });
                return true;
            }
            catch (Win32Exception error) when (error.NativeErrorCode is 1155 or 2)
            {
                // An app can still be chosen when Windows has no usable default handler.
            }
        }
        const int executeWithChosenApp = 4;
        const int cancelled = unchecked((int)0x800704C7);
        const int aborted = unchecked((int)0x80004004);
        var result = SHOpenWithDialog(owner, new OpenAsInfo(path, null, executeWithChosenApp));
        if (result is cancelled or aborted)
            return false;
        Marshal.ThrowExceptionForHR(result);
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct OpenAsInfo(string file, string? fileClass, int flags)
    {
        public readonly string File = file;
        public readonly string? FileClass = fileClass;
        public readonly int Flags = flags;
    }

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(nint owner, in OpenAsInfo info);
}
