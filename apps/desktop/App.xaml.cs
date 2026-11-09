using System.IO;
using System.Windows;

namespace EduCast.Studio;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, args) => Log(args.Exception);
    }

    internal static void Log(Exception exception)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EduCast Studio");
            Directory.CreateDirectory(folder);
            // Keep filenames, project contents and teacher notes out of the local diagnostic log.
            File.AppendAllText(Path.Combine(folder, "studio.log"),
                $"{DateTimeOffset.Now:O} {exception.GetType().Name} {exception.HResult:X8}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
