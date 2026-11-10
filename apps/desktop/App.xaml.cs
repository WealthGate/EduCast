using System.IO;
using System.Windows;

namespace EduCast.Studio;

public partial class App : Application
{
    public App() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EduCast Studio", "theme.txt")) { }

    internal App(string appearancePath)
    {
        Appearance = new Appearance(this, appearancePath);
        DispatcherUnhandledException += (_, args) => Log(args.Exception);
    }

    public Appearance Appearance { get; }

    protected override async void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        if (args.Args is [InAppUpdate.RunnerArgument])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                if (!await InAppUpdate.ApplyAsync())
                    MessageBox.Show("The update installed. Restart Windows, then open EduCast Studio.",
                        "EduCast update", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                Log(error);
                MessageBox.Show($"{error.Message}\n\nYou can open EduCast again from Start or your portable folder.",
                    "EduCast update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { Shutdown(); }
            return;
        }

        Appearance.Load();
        MainWindow = new TeacherWindow();
        MainWindow.Show();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            InAppUpdate.CleanFinished();
        });
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
