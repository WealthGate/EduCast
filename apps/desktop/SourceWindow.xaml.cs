using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EduCast.Classroom;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using ItemVisibility = EduCast.Classroom.Visibility;
using Matrix = System.Windows.Media.Matrix;
using Visibility = System.Windows.Visibility;

namespace EduCast.Studio;

internal partial class SourceWindow : Window
{
    private enum Tool { Interact, Select, Pen, SquarePen, Highlighter, Eraser, Arrow, Pointer }
    private sealed record ColorChoice(string Name, uint Argb);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly Stack<ImmutableArray<Classroom.Content>> undo = new();
    private readonly TeacherWindow teacher;
    private BrowserLesson? browser;
    private Task? browserInitialization;
    private WindowCapture? capture;
    private BitmapSource? image;
    private ImmutableArray<Classroom.Content> marks = [];
    private Classroom.Content? gesture;
    private Point? pointer;
    private Point? arrowStart;
    private Guid? selectedId;
    private BoardSelection? selectionDrag;
    private Matrix? selectionPreview;
    private Tool tool;
    private bool browserMode;
    private bool reading;
    private bool closed;
    private int sourceRevision;

    public event Action<bool>? ProgramChanged;

    public SourceWindow(TeacherWindow teacher)
    {
        this.teacher = teacher;
        Owner = teacher;
        InitializeComponent();
        Width = Math.Clamp(SystemParameters.WorkArea.Width - 36, MinWidth, Width);
        Height = Math.Clamp(SystemParameters.WorkArea.Height - 36, MinHeight, Height);
        Colors.ItemsSource = new ColorChoice[]
        {
            new("Red", 0xFFFF3737), new("Blue", 0xFF2083ED), new("Green", 0xFF138044),
            new("Yellow", 0xFFFFD31A), new("White", 0xFFFFFFFF), new("Black", 0xFF101010)
        };
        Widths.ItemsSource = new[] { 2, 4, 6, 10, 16 };
        Colors.SelectedIndex = 0;
        Widths.SelectedIndex = 1;
        Input.NewStroke = attributes => LiveInkCanvas.Describe(attributes);
        Input.PreviewChanged += next => { gesture = next; RefreshOverlay(); };
        SetTool(Tool.Interact);
        timer.Tick += ReadSource;
        timer.Start();
        Deactivated += (_, _) => CancelAnnotation();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.B && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                teacher.ReturnToBoard();
                args.Handled = true;
            }
            if (args.Key == Key.Escape && (selectionDrag is not null || arrowStart is not null))
            {
                CancelAnnotation();
                args.Handled = true;
            }
        };
        Hint.Text = "Choose Browser or select an open window to preview.";
    }

    public SourceFrame? PublishedFrame(bool includeGestures) => Publish.IsChecked == true && image is { } frame
        ? new SourceFrame(frame, new SourceInk(marks, includeGestures ? gesture : null, includeGestures ? pointer : null)) : null;

    internal void DisplayProgramState(string state, bool frozen, bool blackout)
    {
        SourceProgramState.Text = state;
        FreezeSourceButton.Content = frozen ? "Resume" : "Freeze";
        BlackoutSourceButton.Content = blackout ? "Restore" : "Blackout";
    }

    private void SelectBrowser(object sender, RoutedEventArgs args) => ShowBrowser();
    private void ReturnToBoard(object sender, RoutedEventArgs args) => teacher.ReturnToBoard();
    private void SelectWindow(object sender, RoutedEventArgs args) => ChooseWindow();
    private void TakePreview(object sender, RoutedEventArgs args) => teacher.SourceTake();
    private void FreezeProgram(object sender, RoutedEventArgs args) => teacher.SourceFreeze();
    private void BlackoutProgram(object sender, RoutedEventArgs args) => teacher.SourceBlackout();

    public void ShowBrowser()
    {
        WindowBar.Visibility = Visibility.Collapsed;
        BrowserBar.Visibility = Visibility.Visible;
        if (!browserMode)
        {
            ResetSource();
            browserMode = true;
            BrowserSlot.Visibility = Visibility.Visible;
            SetTool(Tool.Interact);
            Hint.Text = "Enter a web address or open a local HTML, PDF or image file.";
        }
        Address.Focus();
        Address.SelectAll();
    }

    public void ChooseWindow()
    {
        WindowBar.Visibility = Visibility.Visible;
        RefreshWindowList(this, new RoutedEventArgs());
        Windows.Focus();
    }

    private void RefreshWindowList(object sender, RoutedEventArgs args)
    {
        var selected = Windows.SelectedItem as CaptureWindow;
        var windows = CaptureWindow.List();
        Windows.ItemsSource = windows;
        Windows.SelectedItem = windows.FirstOrDefault(window => window.Handle == selected?.Handle);
        if (windows.Count == 0)
            Hint.Text = "No open app windows found. Open a window and refresh the list.";
    }

    private void StartWindowCapture(object sender, RoutedEventArgs args)
    {
        if (Windows.SelectedItem is not CaptureWindow selected)
        {
            Hint.Text = "Select the app window you want to preview.";
            return;
        }
        try
        {
            var next = new WindowCapture(selected);
            ResetSource();
            capture = next;
            browserMode = false;
            BrowserBar.Visibility = Visibility.Collapsed;
            BrowserSlot.Visibility = Visibility.Hidden;
            SetTool(Tool.Pen);
            Hint.Text = $"Waiting for {selected.Title}. Restore the window if it is minimized.";
        }
        catch (Exception error) when (SourceError(error))
        {
            Hint.Text = $"Could not capture that window. {error.Message}";
        }
    }

    public void OpenFile(string path) => OpenPage(page => page.OpenFile(path));

    private void ChooseFile(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog { Filter = BrowserLesson.FileFilter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            OpenFile(dialog.FileName);
    }

    private void Go(object sender, RoutedEventArgs args)
    {
        if (BrowserLesson.WebAddress(Address.Text) is not { } uri)
        {
            Hint.Text = "Enter a valid HTTP or HTTPS web address, or choose Open file for a local lesson.";
            return;
        }
        OpenPage(page => page.Open(uri));
    }

    private void AddressKey(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter)
            return;
        args.Handled = true;
        Go(sender, args);
    }

    private async void OpenPage(Action<BrowserLesson> navigate)
    {
        ShowBrowser();
        ResetSource();
        SetTool(Tool.Interact);
        var revision = sourceRevision;
        Hint.Text = "Opening browser…";
        try
        {
            if (browser is null)
            {
                var page = new BrowserLesson();
                browser = page;
                BrowserSlot.Children.Add(page.View);
                page.Loading += () =>
                {
                    if (!browserMode)
                        return;
                    image = null;
                    ResetAnnotations();
                    Hint.Text = "Loading page…";
                };
                page.Loaded += message =>
                {
                    if (!browserMode)
                        return;
                    Address.Text = page.Address;
                    Hint.Text = message;
                };
                browserInitialization = page.InitializeAsync();
            }
            await browserInitialization!;
            if (!closed && revision == sourceRevision)
                navigate(browser);
        }
        catch (Exception error) when (SourceError(error) || error is WebView2RuntimeNotFoundException)
        {
            if (closed || revision != sourceRevision)
                return;
            if (browserInitialization?.IsFaulted == true && browser is { } failed)
            {
                BrowserSlot.Children.Remove(failed.View);
                failed.Dispose();
                browser = null;
                browserInitialization = null;
            }
            Hint.Text = error is WebView2RuntimeNotFoundException
                ? "Install the Microsoft Edge WebView2 Runtime to open the teaching browser."
                : $"Could not open the page. {error.Message}";
        }
    }

    private void GoBack(object sender, RoutedEventArgs args)
    {
        if (browser?.View.CoreWebView2 is { CanGoBack: true } core)
            core.GoBack();
    }

    private void ReloadPage(object sender, RoutedEventArgs args) => browser?.View.CoreWebView2?.Reload();

    private async void ReadSource(object? sender, EventArgs args)
    {
        if (closed || reading)
            return;
        var revision = sourceRevision;
        reading = true;
        try
        {
            BitmapSource? next;
            if (capture is { } window)
            {
                if (!window.IsOpen)
                {
                    ResetSource();
                    Hint.Text = "That window closed. Choose another window to capture.";
                    return;
                }
                if (window.Target.IsMinimized)
                {
                    if (image is not null)
                    {
                        image = null;
                        Unpublish();
                        RefreshOverlay();
                    }
                    Hint.Text = "The captured window is minimized. Restore it to preview it again.";
                    return;
                }
                if (!IsVisible)
                    return;
                next = await window.ReadAsync();
                if (next is not null && image is null && revision == sourceRevision)
                    Hint.Text = $"{window.Target.Title} · use the drawing tools here and interact in the original window.";
            }
            else if (IsVisible && browserMode && browser is { Ready: true } page && WindowState != WindowState.Minimized)
                next = await page.ReadAsync();
            else
                return;
            if (!closed && revision == sourceRevision && next is not null)
            {
                image = next;
                RefreshOverlay();
            }
        }
        catch (Exception error) when (SourceError(error))
        {
            if (!closed && revision == sourceRevision && IsVisible)
            {
                ResetSource();
                Hint.Text = $"The source stopped. Reopen it to resume. {error.Message}";
            }
        }
        finally { reading = false; }
    }

    private static bool SourceError(Exception error) => error is COMException or InvalidOperationException or IOException or
        ArgumentException or UnauthorizedAccessException;

    private void ChangePublication(object sender, RoutedEventArgs args)
    {
        if (Publish.IsChecked != true)
            Unpublish();
        else
            RefreshOverlay();
    }

    private void Unpublish()
    {
        Publish.IsChecked = false;
        Publish.IsEnabled = image is not null;
        ProgramChanged?.Invoke(true);
    }

    private void ResetSource()
    {
        sourceRevision++;
        image = null;
        Unpublish();
        capture?.Dispose();
        capture = null;
        ResetAnnotations();
    }

    private void ResetAnnotations()
    {
        marks = [];
        selectedId = null;
        undo.Clear();
        CancelAnnotation();
    }

    private void SelectTool(object sender, RoutedEventArgs args) => SetTool(Enum.Parse<Tool>((string)((Button)sender).Tag));

    private void SetTool(Tool next)
    {
        if (next != Tool.Select)
            selectedId = null;
        CancelAnnotation();
        tool = next;
        Drawing.IsHitTestVisible = tool != Tool.Interact;
        Drawing.Cursor = tool switch { Tool.Pointer => Cursors.None, Tool.Select => Cursors.Arrow, _ => Cursors.Cross };
        Input.EditingMode = tool is Tool.Pen or Tool.SquarePen or Tool.Highlighter
            ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        Input.EditingModeInverted = Input.EditingMode;
        Input.UseCustomCursor = true;
        Interact.IsEnabled = browserMode;
        foreach (var button in new[] { Interact, SelectAnnotation, RoundPen, SquarePen, Marker, Eraser, Arrow, Pointer })
            button.Background = (Brush)FindResource((string)button.Tag == tool.ToString() ? "AccentDark" : "Panel");
        ChangeInk(null, null);
    }

    private uint InkColor => ((ColorChoice)Colors.SelectedItem).Argb;
    private int InkWidth => (int)Widths.SelectedItem;
    private Classroom.Content? Selected => marks.FirstOrDefault(item => item.Id == selectedId);
    private double SelectionUnit => BoardSelection.Unit(Math.Min(SourceViewbox.ActualWidth, SourceViewbox.ActualHeight * 16 / 9));

    private void SourceViewSizeChanged(object sender, SizeChangedEventArgs args) => RefreshOverlay();

    private void ChangeInk(object? sender, SelectionChangedEventArgs? args)
    {
        if (Input is null || Colors.SelectedItem is null || Widths.SelectedItem is null)
            return;
        var highlighter = tool == Tool.Highlighter;
        Input.DefaultDrawingAttributes = BoardSurface.InkAttributes(InkColor, highlighter ? Math.Max(12, InkWidth) : InkWidth,
            smooth: true, highlighter, tool switch
            {
                Tool.SquarePen => InkTip.Square, Tool.Highlighter => InkTip.Chisel, _ => InkTip.Round
            });
    }

    private void CollectAnnotation(object sender, InkCanvasStrokeCollectedEventArgs args)
    {
        if (Input.FinishStroke(args.Stroke) is { } stroke && image is not null)
            EditMarks(marks.Add(stroke));
    }

    private void AnnotationDown(object sender, MouseButtonEventArgs args)
    {
        if (image is null)
        {
            args.Handled = true;
            return;
        }
        var position = args.GetPosition(Drawing);
        if (tool == Tool.Select)
        {
            var selection = BoardSelection.Begin(marks, Selected, position, SelectionUnit);
            selectedId = selection?.Item.Id;
            if (selection is not null && Drawing.CaptureMouse())
            {
                selectionDrag = selection;
                Drawing.Cursor = selection.Cursor;
            }
            RefreshOverlay();
            args.Handled = true;
            return;
        }
        if (tool == Tool.Arrow && Drawing.CaptureMouse())
            arrowStart = position;
        if (tool == Tool.Eraser && BoardSurface.Hit(marks, position) is { } item)
            EditMarks(marks.Remove(item));
        args.Handled = tool is Tool.Arrow or Tool.Eraser or Tool.Pointer;
    }

    private void AnnotationMove(object sender, MouseEventArgs args)
    {
        if (image is null)
            return;
        var position = args.GetPosition(Drawing);
        if (tool == Tool.Select)
        {
            if (selectionDrag is not { } drag || !Drawing.IsMouseCaptured)
            {
                Drawing.Cursor = BoardSelection.Hover(Selected, position, SelectionUnit);
                return;
            }
            var transform = drag.Preview(position);
            selectionPreview = transform.IsIdentity ? null : transform;
        }
        else if (tool == Tool.Pointer)
            pointer = position;
        else if (arrowStart is { } start)
            gesture = MakeArrow(start, position);
        else
            return;
        RefreshOverlay();
    }

    private void AnnotationUp(object sender, MouseButtonEventArgs args)
    {
        if (selectionDrag is { } drag)
        {
            var position = args.GetPosition(Drawing);
            CancelAnnotation();
            var replacement = drag.Commit(position);
            if (replacement != drag.Item && marks.Contains(drag.Item))
                EditMarks(marks.Replace(drag.Item, replacement));
            Drawing.Cursor = BoardSelection.Hover(Selected, position, SelectionUnit);
            args.Handled = true;
            return;
        }
        if (arrowStart is not { } start)
            return;
        var end = args.GetPosition(Drawing);
        CancelAnnotation();
        if ((start - end).Length >= 2 && image is not null)
            EditMarks(marks.Add(MakeArrow(start, end)));
        args.Handled = true;
    }

    private Shape MakeArrow(Point start, Point end) => new(Guid.NewGuid(), ItemVisibility.Both, false,
        InkColor, InkWidth, ShapeKind.Arrow, ToBoardPoint(start), ToBoardPoint(end));

    private static BoardPoint ToBoardPoint(Point point) => new(
        Math.Clamp(point.X, 0, BoardSurface.BoardWidth), Math.Clamp(point.Y, 0, BoardSurface.BoardHeight));

    private void AnnotationLeave(object sender, MouseEventArgs args)
    {
        if (pointer is null)
            return;
        pointer = null;
        RefreshOverlay();
    }

    private void AnnotationCaptureLost(object sender, MouseEventArgs args)
    {
        if ((arrowStart is not null || selectionDrag is not null) && !Drawing.IsMouseCaptured)
            CancelAnnotation();
    }

    private void CancelAnnotation()
    {
        Input.CancelStroke();
        (arrowStart, gesture, pointer) = (null, null, null);
        (selectionDrag, selectionPreview) = (null, null);
        if (Drawing.IsMouseCaptured)
            Drawing.ReleaseMouseCapture();
        RefreshOverlay();
    }

    private void EditMarks(ImmutableArray<Classroom.Content> next)
    {
        if (undo.Count >= 100)
            undo.Clear();
        undo.Push(marks);
        marks = next;
        RefreshOverlay();
    }

    private void UndoAnnotation(object sender, RoutedEventArgs args)
    {
        if (undo.TryPop(out var previous))
        {
            marks = previous;
            CancelAnnotation();
        }
    }

    private void ClearAnnotations(object sender, RoutedEventArgs args) => EditMarks([]);

    private void RefreshOverlay()
    {
        var selected = tool == Tool.Select ? Selected : null;
        var selection = selected is null ? null : new AnnotationSelection(selected, selectionPreview, SelectionUnit);
        var preview = selected is not null && selectionPreview is not null ? marks.Remove(selected) : marks;
        Surface.Present(browserMode ? null : image, new SourceInk(preview, gesture as Shape, pointer), selection);
        Publish.IsEnabled = image is not null || Publish.IsChecked == true;
        UndoButton.IsEnabled = undo.Count > 0;
        ClearButton.IsEnabled = !marks.IsEmpty;
        if (Publish.IsChecked == true)
            ProgramChanged?.Invoke(false);
    }

    protected override void OnClosed(EventArgs args)
    {
        closed = true;
        timer.Stop();
        ResetSource();
        browser?.Dispose();
        base.OnClosed(args);
    }
}
