using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    private enum Tool { Interact, Select, Pen, SquarePen, Highlighter, Eraser, Arrow, Pointer, Instrument, Place, SmartInk, SmartMath }
    private sealed record ColorChoice(string Name, uint Argb);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly Stack<ImmutableArray<Classroom.Content>> undo = new();
    private readonly TeacherWindow teacher;
    private readonly SmartInkGroup smartInk;
    private readonly InkTap inkTap;
    private BrowserLesson? browser;
    private Task? browserInitialization;
    private string? browserFile;
    private string? desktopFile;
    private WindowCapture? capture;
    private BitmapSource? image;
    private ImmutableArray<Classroom.Content> marks = [];
    private Classroom.Content? gesture;
    private Point? pointer;
    private Point? arrowStart;
    private Guid? selectedId;
    private BoardSelection? selectionDrag;
    private Matrix? selectionPreview;
    private Classroom.Content? selectionReplacement;
    private Classroom.Content? constructionPreview;
    private string? readingToPlace;
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
        smartInk = new SmartInkGroup(() => marks, EditMarks);
        inkTap = new InkTap(CommitInk);
        Input.NewStroke = attributes => LiveInkCanvas.Describe(attributes);
        Input.StrokeStarted += smartInk.Pause;
        Input.PreviewChanged += next =>
        {
            gesture = next;
            if (next is null)
                smartInk.Resume();
            RefreshOverlay();
        };
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
            if (args.Key == Key.Escape)
            {
                selectedId = null;
                SetTool(Tool.Select);
                args.Handled = true;
            }
        };
        Hint.Text = "Choose Browser or select an open window to preview.";
    }

    public SourceFrame? PublishedFrame(bool includeGestures) => Publish.IsChecked == true && image is { } frame
        ? new SourceFrame(frame, new SourceInk(includeGestures ? MovingMarks() : marks,
            includeGestures ? constructionPreview ?? gesture : null, includeGestures ? pointer : null), mediaId) : null;

    private ImmutableArray<Classroom.Content> MovingMarks() =>
        selectionDrag is { } drag && selectionReplacement is { } moved && marks.Contains(drag.Item)
            ? marks.Replace(drag.Item, moved) : marks;

    internal void HostInstruments(UIElement? panel)
    {
        InstrumentHost.Children.Clear();
        if (panel is not null)
            InstrumentHost.Children.Add(panel);
    }

    private void OpenInstruments(object sender, RoutedEventArgs args) => teacher.OpenInstruments(source: true);

    internal void UseInstrument() => SetTool(Tool.Instrument);

    internal void PresentInstruments(InstrumentDisplay[] displays, InstrumentDisplay? active, Classroom.Content? preview,
        bool trace, (ClockReading Reading, Vector Offset)? clock)
    {
        SourceInstruments.Present(displays, active, preview, trace, clock);
        constructionPreview = preview?.Visibility == ItemVisibility.Both ? preview : null;
    }

    internal void AddConstruction(ImmutableArray<Classroom.Content> items)
    {
        if (image is not null && !items.IsEmpty)
            EditMarks(marks.AddRange(items));
    }

    internal void PlaceReading(string text)
    {
        SetTool(Tool.Place);
        readingToPlace = text;
        Hint.Text = "Click the resource to place the reading. Esc cancels.";
    }

    internal uint AnnotationColor => InkColor;
    internal int AnnotationWidth => InkWidth;

    internal void DisplayProgramState(string state, bool frozen, bool blackout)
    {
        SourceProgramState.Text = state;
        FreezeSourceButton.Content = frozen ? "Resume" : "Freeze";
        BlackoutSourceButton.Content = blackout ? "Restore" : "Blackout";
    }

    private void SelectBrowser(object sender, RoutedEventArgs args) => ShowBrowser();
    private void OpenLiveBroadcast(object sender, RoutedEventArgs args) => teacher.ShowLiveBroadcast();
    private void ReturnToBoard(object sender, RoutedEventArgs args) => teacher.ReturnToBoard();
    private void SelectWindow(object sender, RoutedEventArgs args) => ChooseWindow();
    private void TakePreview(object sender, RoutedEventArgs args) => teacher.SourceTake();
    private void FreezeProgram(object sender, RoutedEventArgs args) => teacher.SourceFreeze();
    private void BlackoutProgram(object sender, RoutedEventArgs args) => teacher.SourceBlackout();

    public void ShowBrowser()
    {
        WindowBar.Visibility = Visibility.Collapsed;
        MediaBar.Visibility = Visibility.Collapsed;
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
        if (Windows.SelectedItem is null && desktopFile is { } path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var matches = windows.Where(window => name.Length > 0 &&
                window.Title.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length == 1)
                Windows.SelectedItem = matches[0];
        }
        if (desktopFile is not null)
            Hint.Text = "Start the lesson or slide show in its app. Refresh the list if needed, then select its window and choose Preview window.";
        else if (windows.Count == 0)
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
            timer.Interval = TimeSpan.FromMilliseconds(40);
            browserMode = false;
            BrowserBar.Visibility = Visibility.Collapsed;
            MediaBar.Visibility = Visibility.Collapsed;
            BrowserSlot.Visibility = Visibility.Hidden;
            SetTool(Tool.Pen);
            Hint.Text = $"Waiting for {selected.Title}. Restore the window if it is minimized.";
        }
        catch (Exception error) when (SourceError(error))
        {
            Hint.Text = $"Could not capture that window. {error.Message}";
        }
    }

    public void OpenFile(string path) => OpenPage(page => page.OpenFile(path), path);

    private void ChooseFile(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog { Filter = TeachingFile.Filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            teacher.OpenSelectedFile(dialog.FileName);
    }

    private void OpenBrowserFileInApp(object sender, RoutedEventArgs args)
    {
        if (browserFile is { } path)
            OpenDesktopFile(path);
    }

    internal bool OpenDesktopFile(string path)
    {
        try
        {
            if (!TeachingFile.OpenInDesktopApp(new WindowInteropHelper(this).Handle, path))
                return false;
        }
        catch (Exception error) when (SourceError(error) || error is Win32Exception)
        {
            Hint.Text = $"Could not open the file in a desktop app. {error.Message}";
            return false;
        }
        ResetSource();
        desktopFile = path;
        browserMode = false;
        BrowserBar.Visibility = Visibility.Collapsed;
        MediaBar.Visibility = Visibility.Collapsed;
        BrowserSlot.Visibility = Visibility.Hidden;
        SetTool(Tool.Pen);
        Windows.SelectedItem = null;
        ChooseWindow();
        return true;
    }

    private void Go(object sender, RoutedEventArgs args)
    {
        if (TeachingFile.LocalPath(Address.Text) is { } path)
        {
            if (File.Exists(path))
                teacher.OpenSelectedFile(path);
            else
                Hint.Text = "That file is unavailable. Check the path or use Open file.";
            return;
        }
        if (BrowserLesson.WebAddress(Address.Text) is not { } uri)
        {
            Hint.Text = "Enter an HTTP or HTTPS address, paste a full file path, or choose Open file.";
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

    private async void OpenPage(Action<BrowserLesson> navigate, string? file = null)
    {
        ShowBrowser();
        ResetSource();
        browserFile = file;
        OpenInDesktopApp.Visibility = file is null ? Visibility.Collapsed : Visibility.Visible;
        if (file is not null)
            Address.Text = file;
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
                    browserFile = page.LocalFile;
                    OpenInDesktopApp.Visibility = browserFile is null ? Visibility.Collapsed : Visibility.Visible;
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
        if (mediaPath is not null)
        {
            RefreshMedia();
            return;
        }
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
        ResetMedia();
        sourceRevision++;
        image = null;
        browserFile = null;
        desktopFile = null;
        OpenInDesktopApp.Visibility = Visibility.Collapsed;
        Unpublish();
        capture?.Dispose();
        capture = null;
        UpdateInteract();
        ResetAnnotations();
    }

    private void ResetAnnotations()
    {
        inkTap.Clear();
        smartInk.Clear();
        marks = [];
        selectedId = null;
        undo.Clear();
        CancelAnnotation();
        teacher.ClearSourceInstruments();
    }

    private void SelectTool(object sender, RoutedEventArgs args)
    {
        var next = Enum.Parse<Tool>((string)((Button)sender).Tag);
        if (next == Tool.Interact && capture is { } window)
        {
            if (!window.Target.TryActivate())
                Hint.Text = "Switch to the original app from the Windows taskbar to interact with it.";
            return;
        }
        SetTool(next);
        if (next == Tool.Interact)
            browser?.View.Focus();
    }

    private void UpdateInteract()
    {
        Interact.IsEnabled = browserMode || capture is not null;
        Interact.Content = capture is null ? "Interact" : "Interact in app";
        Interact.ToolTip = capture is null ? "Use the page's links, buttons, sliders and other controls"
            : "Return to the original app to use its controls and animations";
    }

    private void SetTool(Tool next, bool cancelGesture = true)
    {
        if (next != Tool.Select)
            selectedId = null;
        if (cancelGesture)
            CancelAnnotation();
        tool = next;
        Drawing.IsHitTestVisible = tool != Tool.Interact;
        Drawing.Cursor = tool switch { Tool.Pointer => Cursors.None, Tool.Select => Cursors.Arrow, _ => Cursors.Cross };
        Input.EditingMode = tool is Tool.Pen or Tool.SquarePen or Tool.Highlighter or Tool.SmartInk or Tool.SmartMath
            ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        Input.EditingModeInverted = Input.EditingMode;
        Input.UseCustomCursor = true;
        UpdateInteract();
        foreach (var button in new[] { Interact, SelectAnnotation, RoundPen, SquarePen, Marker, Eraser, Arrow, Pointer, SmartInk, SmartMath })
            button.Background = (Brush)FindResource((string)button.Tag == tool.ToString() ? "AccentDark" : "Control");
        ChangeInk(null, null);
        if (tool is Tool.Select or Tool.Instrument or Tool.SmartInk or Tool.SmartMath)
            Hint.Text = tool switch
            {
                Tool.Instrument => "Drag an instrument to move it, or enable Trace to draw along it.",
                Tool.Select => "Drag an annotation to move it. Double-click an instrument to move it.",
                Tool.SmartInk => "Sketch a shape and pause to clean it up.",
                _ => "Write a math expression and pause to typeset it."
            };
    }

    private uint InkColor => ((ColorChoice)Colors.SelectedItem).Argb;
    private int InkWidth => (int)Widths.SelectedItem;
    private Classroom.Content? Selected => marks.FirstOrDefault(item => item.Id == selectedId);
    private double SelectionUnit => BoardSelection.Unit(SourceStage.ActualWidth);

    private void SourceAreaSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Keep the live browser at its actual display size. Only the WPF ink layer is transformed.
        var width = Math.Min(args.NewSize.Width, args.NewSize.Height * 16 / 9);
        SourceStage.Width = width;
        SourceStage.Height = width * 9 / 16;
        RefreshOverlay();
    }

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
        if (Input.FinishStroke(args.Stroke) is { Points.IsEmpty: false } stroke && image is not null)
            inkTap.Collect(stroke, point => teacher.OverClock(new Point(point.X, point.Y)) ||
                teacher.OverInstrument(point, source: true) || BoardSelection.Hit(marks, new Point(point.X, point.Y)) is not null);
    }

    private void CommitInk(Ink stroke)
    {
        EditMarks(marks.Add(stroke));
        if (tool is Tool.SmartInk or Tool.SmartMath)
            smartInk.Add(stroke, tool == Tool.SmartMath, BoardSubject.General);
    }

    private void AnnotationDown(object sender, MouseButtonEventArgs args)
    {
        if (image is null)
        {
            args.Handled = true;
            return;
        }
        var position = args.GetPosition(Drawing);
        teacher.SetMathSurface(source: true);
        if (args.ClickCount == 2)
            inkTap.Clear();
        else
            inkTap.Flush();
        if (tool == Tool.Place && readingToPlace is { } text)
        {
            SetTool(Tool.Select);
            EditMarks(marks.Add(new BoardText(Guid.NewGuid(), ItemVisibility.Both, false, InkColor, ToBoardPoint(position), 26, text)));
            args.Handled = true;
            return;
        }
        if ((tool == Tool.Select || args.ClickCount == 2) && teacher.BeginClockDrag(position, source: true))
        {
            if (tool != Tool.Select)
            {
                selectedId = null;
                SetTool(Tool.Select, cancelGesture: false);
            }
            if (!Drawing.CaptureMouse())
                teacher.CancelClockDrag(source: true);
            args.Handled = true;
            return;
        }
        if ((tool == Tool.Instrument || args.ClickCount == 2) &&
            teacher.BeginMathDrag(ToBoardPoint(position), source: true, selectOnly: tool != Tool.Instrument))
        {
            if (tool != Tool.Instrument)
            {
                Input.CancelStroke();
                SetTool(Tool.Instrument, cancelGesture: false);
                RefreshOverlay();
            }
            if (!Drawing.CaptureMouse())
                teacher.CancelSourceInstrumentDrag();
            args.Handled = true;
            return;
        }
        if (args.ClickCount == 2 && BoardSelection.Hit(marks, position) is not null)
            SetTool(Tool.Select);
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
        args.Handled = tool is Tool.Arrow or Tool.Eraser or Tool.Pointer or Tool.Instrument;
    }

    private void AnnotationMove(object sender, MouseEventArgs args)
    {
        if (image is null)
            return;
        var position = args.GetPosition(Drawing);
        if (teacher.ClockDragging(source: true) && Drawing.IsMouseCaptured)
        {
            teacher.MoveClock(position);
            return;
        }
        if (teacher.InstrumentDragging(source: true) && Drawing.IsMouseCaptured)
        {
            teacher.UpdateMathDrag(new BoardPoint(position.X, position.Y), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }
        if (tool == Tool.Select)
        {
            if (selectionDrag is not { } drag || !Drawing.IsMouseCaptured)
            {
                Drawing.Cursor = BoardSelection.Hover(Selected, position, SelectionUnit);
                return;
            }
            var transform = drag.Preview(position);
            selectionPreview = transform.IsIdentity ? null : transform;
            selectionReplacement = transform.IsIdentity ? null : drag.Commit(position);
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
        var pointerPosition = args.GetPosition(Drawing);
        if (teacher.ClockDragging(source: true))
        {
            teacher.MoveClock(pointerPosition);
            teacher.FinishClockDrag();
            Drawing.ReleaseMouseCapture();
            args.Handled = true;
            return;
        }
        if (teacher.InstrumentDragging(source: true))
        {
            teacher.EndMathDrag(new BoardPoint(pointerPosition.X, pointerPosition.Y),
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            Drawing.ReleaseMouseCapture();
            args.Handled = true;
            return;
        }
        if (selectionDrag is { } drag)
        {
            var position = args.GetPosition(Drawing);
            (selectionDrag, selectionPreview, selectionReplacement) = (null, null, null);
            Drawing.ReleaseMouseCapture();
            var replacement = drag.Commit(position);
            if (replacement != drag.Item && marks.Contains(drag.Item))
                EditMarks(marks.Replace(drag.Item, replacement));
            else
                RefreshOverlay();
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

    private void AnnotationWheel(object sender, MouseWheelEventArgs args)
    {
        if (tool == Tool.Instrument)
            args.Handled = teacher.SourceGaugeWheel(args.GetPosition(Drawing), args.Delta,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private void AnnotationCaptureLost(object sender, MouseEventArgs args)
    {
        if ((arrowStart is not null || selectionDrag is not null || teacher.ClockDragging(source: true) ||
            teacher.InstrumentDragging(source: true)) && !Drawing.IsMouseCaptured)
            CancelAnnotation();
    }

    private void CancelAnnotation()
    {
        inkTap.Flush();
        smartInk.Complete();
        teacher.CancelClockDrag(source: true);
        teacher.CancelSourceInstrumentDrag();
        readingToPlace = null;
        Input.CancelStroke();
        (arrowStart, gesture, pointer) = (null, null, null);
        (selectionDrag, selectionPreview, selectionReplacement) = (null, null, null);
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
        inkTap.Flush();
        smartInk.Clear();
        if (undo.TryPop(out var previous))
        {
            marks = previous;
            CancelAnnotation();
        }
    }

    private void ClearAnnotations(object sender, RoutedEventArgs args)
    {
        inkTap.Clear();
        smartInk.Clear();
        EditMarks([]);
    }

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
