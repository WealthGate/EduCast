using System.ComponentModel;
using System.IO;
using System.Windows;
using EduCast.Classroom;

namespace EduCast.Studio;

public enum WorkspaceTheme { Violet, Ocean, Daylight }

public sealed class Appearance : INotifyPropertyChanged
{
    private readonly Application app;
    private readonly string path;
    private WorkspaceTheme selected;

    internal Appearance(Application app, string path) => (this.app, this.path) = (app, path);

    public IReadOnlyList<WorkspaceTheme> Choices { get; } = Enum.GetValues<WorkspaceTheme>();

    public WorkspaceTheme Selected
    {
        get => selected;
        set
        {
            if (Apply(value))
                Save();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Load()
    {
        try
        {
            if (File.Exists(path) && Enum.TryParse<WorkspaceTheme>(File.ReadAllText(path).Trim(), out var saved))
                Apply(saved);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private bool Apply(WorkspaceTheme theme)
    {
        if (theme == selected || !Enum.IsDefined(theme))
            return false;
        var palette = new ResourceDictionary { Source = new Uri($"/EduCast.Studio;component/Themes/{theme}.xaml", UriKind.Relative) };
        app.Resources.MergedDictionaries[0] = palette;
        selected = theme;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        return true;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.Write(path, stream =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(selected);
            });
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
