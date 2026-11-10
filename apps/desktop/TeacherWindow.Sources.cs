using System.Windows;
using System.Windows.Input;

namespace EduCast.Studio;

public partial class TeacherWindow
{
    private SourceWindow? sourceWindow;
    private SourceFrame? takenSource;
    private Point? boardPointer;

    private SourceWindow ShowSourceWindow()
    {
        if (sourceWindow is null)
        {
            var window = new SourceWindow(this);
            sourceWindow = window;
            window.ProgramChanged += SourceChanged;
            window.Closed += (_, _) =>
            {
                sourceWindow = null;
                SourceChanged(revoke: true);
            };
            window.Show();
            window.DisplayProgramState(ProgramState.Text, session.Frozen, session.Blackout);
        }
        if (sourceWindow.WindowState == WindowState.Minimized)
            sourceWindow.WindowState = WindowState.Normal;
        sourceWindow.Activate();
        return sourceWindow;
    }

    private void OpenBrowser(object sender, RoutedEventArgs args) => ShowSourceWindow().ShowBrowser();

    private void OpenWindowCapture(object sender, RoutedEventArgs args) => ShowSourceWindow().ChooseWindow();

    private void SourceChanged(bool revoke)
    {
        if (revoke)
        {
            takenSource = null;
            if (frozenLayers is { } held)
                frozenLayers = held with { Source = null };
        }
        RefreshStudioLayers();
    }

    private void TakeSource() => takenSource = sourceWindow?.PublishedFrame(includeGestures: false);

    internal void SourceTake() => Take(this, new RoutedEventArgs());
    internal void SourceFreeze() => Freeze(this, new RoutedEventArgs());
    internal void SourceBlackout() => Blackout(this, new RoutedEventArgs());

    private void BoardLeave(object sender, MouseEventArgs args) => SetBoardPointer(null);

    private void SetBoardPointer(Point? position)
    {
        if (boardPointer == position)
            return;
        boardPointer = position;
        TeacherBoard.MarkPointer(position);
        RefreshStudioLayers();
    }
}
