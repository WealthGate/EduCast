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
using Visibility = System.Windows.Visibility;

namespace EduCast.Studio;

internal partial class SourceWindow : Window
{
    private enum Tool { Interact, Select, Pen, SquarePen, Highlighter, Eraser, Arrow, Pointer, Instrument, Place, SmartInk, SmartMath, Type }
    private sealed record ColorChoice(string Name, uint Argb);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly Stack<ImmutableArray<Classroom.Content>> undo = new();
    private readonly TeacherWindow teacher;
    private readonly TeachingResources resources = new();
    private readonly SmartInkGroup smartInk;
    private readonly InkTap inkTap;
    private readonly CanvasClick canvasClick = new();
    private readonly CanvasEraser eraser;
    private readonly CanvasTextEditor textEditor;
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
    private Point? marqueeStart;
    private ImmutableHashSet<Guid> selectedIds = [];
    private BoardSelection? selectionDrag;
    private ImmutableArray<Classroom.Content> selectionPreview = [];
    private Classroom.Content? constructionPreview;
    private Func<BoardPoint, Classroom.Content>? placement;
    private Tool tool;
    private bool browserMode;
    private bool reading;
    private bool closed;
    private bool temporaryInteraction;
    private bool marqueeEnabled;
    private int sourceRevision;

    public event Action<bool>? ProgramChanged;

    public SourceWindow(TeacherWindow teacher)
    {
        this.teacher = teacher;
        Owner = teacher;
        InitializeComponent();
        textEditor = new CanvasTextEditor(SourceTextTools, SourceTextOverlay);
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
        eraser = new CanvasEraser(Drawing, AnnotationFeedback, EraserSize, () => SelectionUnit, () => marks,
            RefreshOverlay, next =>
            {
                if (next != marks)
                    EditMarks(next);
                else
                    RefreshOverlay();
            });
        eraser.Attach();
        Input.NewStroke = attributes => LiveInkCanvas.Describe(attributes);
        AnnotationHits.ContainsAnnotation = AnnotationAt;
        AnnotationHits.Cursor = Cursors.Hand;
        Input.StrokeStarted += smartInk.Pause;
        Input.PreviewChanged += next =>
        {
            gesture = next;
            if (next is null)
                smartInk.Resume();
            RefreshOverlay();
        };
        SourceInstruments.RemoveInstrument += () => teacher.RemoveActiveInstrument(source: true);
        SourceInstruments.RemoveClock += teacher.RemoveClock;
        SetTool(Tool.Interact);
        timer.Tick += ReadSource;
        timer.Start();
        Deactivated += (_, _) =>
        {
            EndResourceInteraction();
            CancelAnnotation(finishText: false);
        };
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(ResourceInteractionDown), true);
        AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(ResourceInteractionUp), true);
        PreviewKeyDown += (_, args) =>
        {
            if (textEditor.Shortcut(args))
                return;
            if (args.Key == Key.B && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                teacher.ReturnToBoard();
                args.Handled = true;
            }
            if (args.Key == Key.Escape)
            {
                selectedIds = [];
                SetTool(Tool.Select, enableMarquee: true);
                args.Handled = true;
            }
        };
        Hint.Text = "Choose Browser or select an open window to preview.";
    }

    public SourceFrame? PublishedFrame(bool includeGestures) => Publish.IsChecked == true && image is { } frame
        ? new SourceFrame(frame, new SourceInk(includeGestures ? MovingMarks() : marks,
            includeGestures ? constructionPreview ?? gesture : null, includeGestures ? pointer : null), mediaId) : null;

    private ImmutableArray<Classroom.Content> MovingMarks() => BoardSelection.Apply(marks, selectionPreview);

    internal void HostInstruments(UIElement? panel)
    {
        InstrumentHost.Children.Clear();
        if (panel is not null)
            InstrumentHost.Children.Add(panel);
    }

    private void OpenInstruments(object sender, RoutedEventArgs args) => teacher.ToggleInstruments(source: true);

    internal void SetInstrumentExpansion(bool expanded) =>
        SourceMathToolsButton.Content = expanded ? "▾ Instruments" : "▸ Instruments";

    internal void UseInstrument() => SetTool(Tool.Instrument);

    internal void PresentInstruments(InstrumentDisplay[] displays, InstrumentDisplay? active, Classroom.Content? preview,
        bool trace, (ClockReading Reading, Vector Offset)? clock)
    {
        SourceInstruments.Present(displays, active, preview, trace, clock, SelectionUnit);
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
        placement = origin => new BoardText(Guid.NewGuid(), ItemVisibility.Both, false, InkColor, origin, 26, text);
        Hint.Text = "Click the resource to place the reading. Esc cancels.";
    }

    internal bool HasMotion => IsVisible && Surface.HasMotion;
    internal void SetMotionTime(double time) => Surface.SetMotionTime(time);

    private void InsertEffect(object sender, RoutedEventArgs args)
    {
        if (image is null)
            return;
        textEditor.Commit();
        var library = new EffectLibrary(this, teacher.CurrentSubject);
        if (library.ShowDialog() != true || library.Result is not { } effect)
            return;
        SetTool(Tool.Place);
        placement = origin => effect.Place(origin, ItemVisibility.Both);
        Hint.Text = "Click the resource to place the effect. Esc cancels.";
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

    internal void OpenResource(TeachingResource resource)
    {
        try
        {
            var (path, view) = resources.Open(resource);
            OpenPage(page => page.OpenFile(path, view), path);
        }
        catch (Exception error) when (SourceError(error))
        {
            Hint.Text = $"Could not open the teaching resource. {error.Message}";
        }
    }

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
        OpenInDesktopApp.Visibility = file is null || resources.Contains(file) ? Visibility.Collapsed : Visibility.Visible;
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
                    OpenInDesktopApp.Visibility = browserFile is null || resources.Contains(browserFile)
                        ? Visibility.Collapsed : Visibility.Visible;
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
        textEditor.Cancel();
        inkTap.Clear();
        smartInk.Clear();
        marks = [];
        selectedIds = [];
        undo.Clear();
        CancelAnnotation();
        teacher.ClearSourceInstruments();
    }

    private void SelectTool(object sender, RoutedEventArgs args)
    {
        var next = Enum.Parse<Tool>((string)((Button)sender).Tag);
        if (next == Tool.Interact && capture is { } window)
        {
            ActivateCapturedApp(window);
            return;
        }
        SetTool(next, enableMarquee: next == Tool.Select);
        if (next == Tool.Interact)
            browser?.View.Focus();
    }

    private void UpdateInteract()
    {
        Interact.IsEnabled = browserMode || capture is not null;
        Interact.Content = capture is null ? "Interact" : "Interact in app";
        Interact.ToolTip = capture is null ? "Hold F4 to use the page while any annotation tool is selected"
            : "Press F4 to use the controls in the original app";
    }

    private void ActivateCapturedApp(WindowCapture window)
    {
        if (!window.Target.TryActivate())
            Hint.Text = "Switch to the original app from the Windows taskbar to interact with it.";
    }

    private void ResourceInteractionDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.F4 || Keyboard.Modifiers != ModifierKeys.None)
            return;
        if (capture is { } window)
        {
            args.Handled = true;
            if (!args.IsRepeat)
                ActivateCapturedApp(window);
        }
        else if (browserMode)
        {
            args.Handled = true;
            if (temporaryInteraction)
                return;
            CancelAnnotation(finishText: false);
            temporaryInteraction = true;
            UpdateAnnotationInput();
        }
    }

    private void ResourceInteractionUp(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.F4 || !temporaryInteraction)
            return;
        args.Handled = true;
        EndResourceInteraction();
    }

    private void EndResourceInteraction()
    {
        if (!temporaryInteraction)
            return;
        temporaryInteraction = false;
        UpdateAnnotationInput();
    }

    private void UpdateAnnotationInput()
    {
        var fullCanvas = !temporaryInteraction && tool != Tool.Interact && !BrowserClickThrough;
        Drawing.IsHitTestVisible = fullCanvas;
        AnnotationHits.IsHitTestVisible = BrowserClickThrough;
        SourceInstruments.IsHitTestVisible = fullCanvas;
        SourceTextOverlay.Visibility = temporaryInteraction ? Visibility.Hidden : Visibility.Visible;
        eraser.Activate(fullCanvas && tool == Tool.Eraser);
    }

    private bool BrowserClickThrough => browserMode && !temporaryInteraction &&
        (tool is Tool.Interact or Tool.Pointer || tool == Tool.Select && !marqueeEnabled);

    private FrameworkElement AnnotationInput => BrowserClickThrough ? AnnotationHits : Drawing;

    private bool AnnotationCaptured => Drawing.IsMouseCaptured || AnnotationHits.IsMouseCaptured;

    private void ReleaseAnnotationCapture()
    {
        if (Drawing.IsMouseCaptured)
            Drawing.ReleaseMouseCapture();
        if (AnnotationHits.IsMouseCaptured)
            AnnotationHits.ReleaseMouseCapture();
    }

    private void SetTool(Tool next, bool enableMarquee = false)
    {
        if (next != Tool.Select)
            selectedIds = [];
        CancelAnnotation();
        tool = next;
        marqueeEnabled = enableMarquee;
        Input.Cursor = tool switch { Tool.Pointer => Cursors.None, Tool.Select => Cursors.Arrow,
            Tool.Type => Cursors.IBeam, _ => Cursors.Cross };
        EraserSize.Visibility = tool == Tool.Eraser ? Visibility.Visible : Visibility.Collapsed;
        UpdateAnnotationInput();
        Input.EditingMode = tool is Tool.Pen or Tool.SquarePen or Tool.Highlighter or Tool.SmartInk or Tool.SmartMath
            ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        Input.EditingModeInverted = Input.EditingMode;
        Input.UseCustomCursor = true;
        UpdateInteract();
        foreach (var button in new[] { Interact, SelectAnnotation, RoundPen, SquarePen, Marker, Eraser, Arrow, Pointer, SmartInk,
            SmartMath, TypeButton })
            button.SetResourceReference(Control.BackgroundProperty, (string)button.Tag == tool.ToString() ? "AccentDark" : "Control");
        ChangeInk(null, null);
        var hint = tool switch
        {
            Tool.Instrument => "Drag an instrument to move it, or enable Trace to draw along it.",
            Tool.Select => "Double-click an annotation or drag a box around a group. Then drag to move or resize.",
            Tool.Eraser => "Choose an eraser size and drag across writing.",
            Tool.Type => "Click the resource to type or click existing text to edit. Choose Done to apply.",
            Tool.SmartInk => "Sketch a shape and pause to clean it up.",
            Tool.SmartMath => "Write a math expression and pause to typeset it.",
            _ => ""
        };
        var interactHint = capture is not null ? "Press F4 to use the captured app."
            : browserMode ? "Hold F4 to use the page through annotations." : "";
        if (hint.Length > 0 || interactHint.Length > 0)
            Hint.Text = (hint + " " + interactHint).Trim();
    }

    private uint InkColor => ((ColorChoice)Colors.SelectedItem).Argb;
    private int InkWidth => (int)Widths.SelectedItem;
    private ImmutableArray<Classroom.Content> Selected => selectedIds.IsEmpty ? [] :
        marks.Where(item => selectedIds.Contains(item.Id)).ToImmutableArray();
    private double SelectionUnit => BoardSelection.Unit(SourceStage.ActualWidth);

    private void SourceAreaSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Keep the live browser at its actual display size. Only the WPF ink layer is transformed.
        var width = Math.Min(args.NewSize.Width, args.NewSize.Height * 16 / 9);
        SourceStage.Width = width;
        SourceStage.Height = width * 9 / 16;
        SourceInstruments.ScaleControls(BoardSelection.Unit(width));
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
        if (Input.FinishStroke(args.Stroke) is not { Points.IsEmpty: false } stroke || image is null ||
            inkTap.Collect(stroke, SelectionUnit) is not { } hit)
            return;
        SelectCanvasTarget(hit);
    }

    private void SelectCanvasTarget(CanvasTarget hit)
    {
        teacher.SetMathSurface(source: true);
        SetTool(hit.Kind == CanvasTargetKind.Instrument && teacher.SelectInstrument(hit.Id, source: true)
            ? Tool.Instrument : Tool.Select);
        selectedIds = hit.Kind == CanvasTargetKind.Content ? [hit.Id] : [];
        RefreshOverlay();
    }

    internal bool TrySelectAt(Point position, int clickCount)
    {
        var target = teacher.TargetAt(marks, position, source: true);
        if (!canvasClick.Begin(clickCount, target, position) || target is not { } hit)
            return false;
        SelectCanvasTarget(hit);
        return true;
    }

    private bool AnnotationAt(Point position)
    {
        if (image is null)
            return false;
        if (tool == Tool.Select)
        {
            var cursor = BoardSelection.Hover(BoardSelection.BoundsOf(Selected), position, SelectionUnit);
            if (cursor != Cursors.Arrow)
                return true;
        }
        return teacher.TargetAt(marks, position, source: true).HasValue;
    }

    private void ResourceMove(object sender, MouseEventArgs args)
    {
        var position = args.GetPosition(Drawing);
        if (args.LeftButton == MouseButtonState.Pressed)
            canvasClick.Move(position, SelectionUnit);
        if (tool == Tool.Pointer && BrowserClickThrough && image is not null)
        {
            pointer = position;
            RefreshOverlay();
        }
    }

    private void CommitInk(Ink stroke)
    {
        EditMarks(marks.Add(stroke));
        if (tool is Tool.SmartInk or Tool.SmartMath)
            smartInk.Add(stroke, tool == Tool.SmartMath, BoardSubject.General);
    }

    private void AnnotationDown(object sender, MouseButtonEventArgs args)
    {
        if (textEditor.Active)
        {
            textEditor.Commit();
            args.Handled = true;
            return;
        }
        if (image is null)
        {
            args.Handled = true;
            return;
        }
        var position = args.GetPosition(Drawing);
        teacher.SetMathSurface(source: true);
        if (tool == Tool.Select && BoardSelection.CloseAt(BoardSelection.BoundsOf(Selected), position, SelectionUnit))
        {
            var selected = Selected;
            CancelAnnotation();
            selectedIds = [];
            EditMarks(marks.RemoveRange(selected));
            args.Handled = true;
            return;
        }
        if (Input.EditingMode == InkCanvasEditingMode.Ink)
        {
            var mouse = args.StylusDevice is null;
            var target = mouse ? teacher.TargetAt(marks, position, source: true) : null;
            inkTap.Begin(args.ClickCount, target, mouse);
            return;
        }
        if (TrySelectAt(position, args.ClickCount))
        {
            args.Handled = true;
            return;
        }
        if (BrowserClickThrough && tool != Tool.Select)
        {
            args.Handled = true;
            return;
        }
        if (tool == Tool.Eraser)
            return;
        if (tool == Tool.Type)
        {
            canvasClick.Single(() => BeginSourceText(position));
            args.Handled = true;
            return;
        }
        if (tool == Tool.Place && placement is { } create)
        {
            canvasClick.Single(() =>
            {
                SetTool(Tool.Select);
                var item = create(ToBoardPoint(position));
                selectedIds = [item.Id];
                EditMarks(marks.Add(item));
            });
            args.Handled = true;
            return;
        }
        if (tool == Tool.Select && teacher.BeginClockDrag(position, source: true))
        {
            if (!AnnotationInput.CaptureMouse())
                teacher.CancelClockDrag(source: true);
            args.Handled = true;
            return;
        }
        if (tool == Tool.Instrument && teacher.BeginMathDrag(ToBoardPoint(position), source: true))
        {
            if (!AnnotationInput.CaptureMouse())
                teacher.CancelSourceInstrumentDrag();
            args.Handled = true;
            return;
        }
        if (tool == Tool.Select)
        {
            var selection = BoardSelection.BeginSelected(Selected, position, SelectionUnit);
            if (selection is null && BrowserClickThrough)
            {
                args.Handled = true;
                return;
            }
            if (AnnotationInput.CaptureMouse())
            {
                selectionDrag = selection;
                if (selectionDrag is { } drag)
                    SetSelectionCursor(drag.Cursor);
                else
                {
                    marqueeStart = position;
                    selectedIds = [];
                }
            }
            RefreshOverlay();
            args.Handled = true;
            return;
        }
        if (tool == Tool.Arrow && AnnotationInput.CaptureMouse())
            arrowStart = position;
        args.Handled = tool is Tool.Arrow or Tool.Pointer or Tool.Instrument;
    }

    private void AnnotationMove(object sender, MouseEventArgs args)
    {
        if (image is null || tool == Tool.Eraser)
            return;
        var position = args.GetPosition(Drawing);
        if (teacher.ClockDragging(source: true) && AnnotationCaptured)
        {
            teacher.MoveClock(position);
            return;
        }
        if (teacher.InstrumentDragging(source: true) && AnnotationCaptured)
        {
            teacher.UpdateMathDrag(new BoardPoint(position.X, position.Y), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }
        if (tool == Tool.Select)
        {
            if (selectionDrag is not { } drag || !AnnotationCaptured)
            {
                if (marqueeStart is { } origin)
                    AnnotationFeedback.ShowMarquee(new Rect(origin, position), SelectionUnit);
                else
                    SetSelectionCursor(BoardSelection.Hover(BoardSelection.BoundsOf(Selected), position, SelectionUnit));
                return;
            }
            var moved = drag.CommitAll(position);
            if (selectionPreview.IsEmpty && moved == drag.Items)
                return;
            selectionPreview = moved == drag.Items ? [] : moved;
        }
        else if (tool == Tool.Pointer && !BrowserClickThrough)
            pointer = position;
        else if (arrowStart is { } start)
            gesture = MakeArrow(start, position);
        else
            return;
        RefreshOverlay();
    }

    private void AnnotationUp(object sender, MouseButtonEventArgs args)
    {
        if (tool == Tool.Eraser)
            return;
        var pointerPosition = args.GetPosition(Drawing);
        if (teacher.ClockDragging(source: true))
        {
            teacher.MoveClock(pointerPosition);
            teacher.FinishClockDrag();
            ReleaseAnnotationCapture();
            args.Handled = true;
            return;
        }
        if (teacher.InstrumentDragging(source: true))
        {
            teacher.EndMathDrag(new BoardPoint(pointerPosition.X, pointerPosition.Y),
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            ReleaseAnnotationCapture();
            args.Handled = true;
            return;
        }
        if (selectionDrag is { } drag)
        {
            (selectionDrag, selectionPreview) = (null, []);
            ReleaseAnnotationCapture();
            var moved = drag.CommitAll(pointerPosition);
            if (moved != drag.Items)
                EditMarks(BoardSelection.Apply(marks, moved));
            else
                RefreshOverlay();
            SetSelectionCursor(BoardSelection.Hover(BoardSelection.BoundsOf(Selected), pointerPosition, SelectionUnit));
            args.Handled = true;
            return;
        }
        if (marqueeStart is { } origin)
        {
            marqueeStart = null;
            ReleaseAnnotationCapture();
            AnnotationFeedback.ShowMarquee(Rect.Empty, SelectionUnit);
            selectedIds = BoardSelection.Within(marks, origin, pointerPosition, SelectionUnit)
                .Select(item => item.Id).ToImmutableHashSet();
            RefreshOverlay();
            SetSelectionCursor(BoardSelection.Hover(BoardSelection.BoundsOf(Selected), pointerPosition, SelectionUnit));
            args.Handled = true;
            return;
        }
        if (arrowStart is not { } start)
            return;
        var end = args.GetPosition(Drawing);
        (arrowStart, gesture) = (null, null);
        ReleaseAnnotationCapture();
        if (CanvasClick.IsDrag(start, end, SelectionUnit) && image is not null)
        {
            canvasClick.Clear();
            EditMarks(marks.Add(MakeArrow(start, end)));
        }
        else
            RefreshOverlay();
        args.Handled = true;
    }

    private Shape MakeArrow(Point start, Point end) => new(Guid.NewGuid(), ItemVisibility.Both, false,
        InkColor, InkWidth, ShapeKind.Arrow, ToBoardPoint(start), ToBoardPoint(end));

    private void SetSelectionCursor(Cursor cursor)
    {
        Input.Cursor = cursor;
        AnnotationHits.Cursor = cursor == Cursors.Arrow ? Cursors.Hand : cursor;
    }

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
        if ((arrowStart is not null || marqueeStart is not null || selectionDrag is not null || teacher.ClockDragging(source: true) ||
            teacher.InstrumentDragging(source: true)) && !AnnotationCaptured)
            CancelAnnotation();
    }

    private void CancelAnnotation(bool finishText = true)
    {
        if (finishText)
            textEditor.Commit();
        canvasClick.Clear();
        inkTap.Flush();
        smartInk.Complete();
        teacher.CancelClockDrag(source: true);
        teacher.CancelSourceInstrumentDrag();
        placement = null;
        Input.CancelStroke();
        eraser.Cancel();
        (arrowStart, gesture, pointer) = (null, null, null);
        (marqueeStart, selectionDrag, selectionPreview) = (null, null, []);
        AnnotationFeedback.ShowMarquee(Rect.Empty, SelectionUnit);
        ReleaseAnnotationCapture();
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

    internal void BeginSourceText(Point position)
    {
        var existing = BoardSelection.Hit(marks, position) as BoardText;
        var text = existing ?? CanvasTextEditor.New(position, InkColor, ItemVisibility.Both);
        textEditor.Open(text, next =>
        {
            if (existing is null)
                EditMarks(marks.Add(next));
            else if (next != existing && marks.Contains(existing))
                EditMarks(marks.Replace(existing, next));
        });
    }

    private void UndoAnnotation(object sender, RoutedEventArgs args)
    {
        inkTap.Flush();
        smartInk.Clear();
        CancelAnnotation();
        if (undo.TryPop(out var previous))
        {
            marks = previous;
            RefreshOverlay();
        }
    }

    private void ClearAnnotations(object sender, RoutedEventArgs args)
    {
        inkTap.Clear();
        smartInk.Clear();
        CancelAnnotation();
        selectedIds = [];
        EditMarks([]);
    }

    private void RefreshOverlay()
    {
        var selected = !selectionPreview.IsEmpty ? selectionPreview : Selected;
        var bounds = BoardSelection.BoundsOf(selected);
        var selection = tool == Tool.Select && !bounds.IsEmpty ? new AnnotationSelection(bounds, SelectionUnit) : null;
        Surface.Present(browserMode ? null : image, new SourceInk(eraser.Draft ?? MovingMarks(), gesture as Shape, pointer), selection);
        Publish.IsEnabled = image is not null || Publish.IsChecked == true;
        TypeButton.IsEnabled = EffectsButton.IsEnabled = image is not null;
        UndoButton.IsEnabled = undo.Count > 0;
        ClearButton.IsEnabled = !marks.IsEmpty;
        if (Publish.IsChecked == true)
            ProgramChanged?.Invoke(false);
        teacher.RefreshAnimation();
    }

    protected override void OnClosed(EventArgs args)
    {
        closed = true;
        timer.Stop();
        ResetSource();
        browser?.Dispose();
        resources.Dispose();
        base.OnClosed(args);
    }
}
