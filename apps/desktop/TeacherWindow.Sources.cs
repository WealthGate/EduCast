using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace EduCast.Studio;

public partial class TeacherWindow
{
    private SourceWindow? sourceWindow;
    private SourceFrame? takenSource;
    private Point? boardPointer;
    private bool sourceOnStage;
    private IntPtr boardShortcutWindow;
    private const int HotkeyMessage = 0x0312;
    private const int BoardHotkeyId = 1;

    private void RegisterBoardShortcut(IntPtr window)
    {
        const uint controlShiftNoRepeat = 0x0002 | 0x0004 | 0x4000;
        if (RegisterHotKey(window, BoardHotkeyId, controlShiftNoRepeat, 0x42))
            boardShortcutWindow = window;
        else
            BoardButton.ToolTip = "Return to the writing board. Another app is using the global Ctrl+Shift+B shortcut.";
    }

    private void UnregisterBoardShortcut()
    {
        if (boardShortcutWindow != IntPtr.Zero)
            UnregisterHotKey(boardShortcutWindow, BoardHotkeyId);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private void ShowBoard(object sender, RoutedEventArgs args) => ReturnToBoard();

    internal void ReturnToBoard()
    {
        if (!IsEnabled)
            return;
        SetSourceStage(false);
        if (sourceWindow?.IsVisible == true)
            sourceWindow.Hide();
        if (studio?.IsVisible == true)
            studio.Hide();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        if (!IsInkTool(tool))
        {
            CancelGesture();
            SetTool(Tool.Pen);
            Refresh();
        }
        DrawingInput.Focus();
    }

    private void ResumeSource(object sender, RoutedEventArgs args)
    {
        var existing = sourceWindow is not null;
        var window = ShowSourceWindow();
        if (!existing)
            window.ShowBrowser();
        SetSourceStage(true);
    }

    private void SetSourceStage(bool onStage)
    {
        sourceOnStage = onStage;
        ResumeSourceButton.Content = sourceWindow is null ? "Teaching source" : "Resume source";
        RefreshStudioLayers();
    }

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
                SetSourceStage(false);
                SourceChanged(revoke: true);
            };
            window.Show();
            window.DisplayProgramState(ProgramState.Text, session.Frozen, session.Blackout);
        }
        if (!sourceWindow.IsVisible)
            sourceWindow.Show();
        if (sourceWindow.WindowState == WindowState.Minimized)
            sourceWindow.WindowState = WindowState.Normal;
        sourceWindow.Activate();
        return sourceWindow;
    }

    private void OpenBrowser(object sender, RoutedEventArgs args)
    {
        ShowSourceWindow().ShowBrowser();
        SetSourceStage(true);
    }

    private void OpenWindowCapture(object sender, RoutedEventArgs args)
    {
        ShowSourceWindow().ChooseWindow();
        SetSourceStage(true);
    }

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

    private void TakeSource()
    {
        if (sourceOnStage)
            takenSource = sourceWindow?.PublishedFrame(includeGestures: false);
    }

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
