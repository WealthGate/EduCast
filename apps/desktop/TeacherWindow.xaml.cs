using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EduCast.Classroom;
using Microsoft.Win32;
using BoardItem = EduCast.Classroom.Content;
using ItemVisibility = EduCast.Classroom.Visibility;
using Page = EduCast.Classroom.Page;

namespace EduCast.Studio;

public partial class TeacherWindow : Window
{
    private enum Tool { Pen, SquarePen, Highlighter, Eraser, Arrow, Pointer, Select, Line, Circle, Arc, Rectangle,
        Triangle, Ellipse, Resistor, Capacitor, Equal, DoubleBond, TripleBond, Polygon, Cone, Sphere, Cube, Cuboid, Cylinder,
        Place, Instrument, SmartInk, SmartMath }
    private sealed record ColorChoice(string Name, uint Argb);
    private sealed record OutputChoice(Display? Display)
    {
        public string Label => Display?.Label ?? "Windowed output";
    }
    private sealed record ObjectChoice(Guid Id, string Label);
    private sealed record VisibilityOption(ItemVisibility Value, string Label);

    private static readonly ColorChoice[] Palette =
    [
        new("Auto ink", 0xff172c39), new("Blue", 0xff145ac1), new("Teal", 0xff117969),
        new("Red", 0xffb32f47), new("Amber", 0xffc06e15), new("Violet", 0xff8142bd),
        new("Black", 0xff0e1118), new("Green", 0xff207b38), new("Pink", 0xffd54388),
        new("Orange", 0xffe95823), new("Cyan", 0xff18bcd0), new("Yellow", 0xffffdc56),
        new("White", 0xfff5fbff)
    ];

    private readonly Session session = new();
    private readonly CancellationTokenSource updateLifetime = new();
    private readonly Func<CancellationToken, Task<AvailableUpdate?>> checkRelease;
    private AvailableUpdate? availableUpdate;
    private bool checkingUpdate;
    private bool applyingUpdate;
    private Lesson saved;
    private Page? renderedPage;
    private ProgramFrame? renderedOutput;
    private Ink? liveStroke;
    private bool liveInkShown;
    private ProgramInk? programInk;
    private ProgramLayers studioLayers = new();
    private ProgramLayers visibleLayers = new();
    private ProgramLayers? frozenLayers;
    private StudioTools? studio;
    private ProgramRecording? recording;
    private readonly DispatcherTimer recordingClock;
    private bool stoppingRecording;
    private bool closeAfterRecording;
    private string? projectPath;
    private DisplayWindow? output;
    private Display? activeDisplay;
    private string? preferredDisplay;
    private Guid? selectedId;
    private ImmutableHashSet<Guid> groupIds = [];
    private Tool tool = Tool.Pen;
    private int polygonSides = 5;
    private Tool toolBeforePlacement;
    private Func<BoardPoint, BoardItem>? placement;
    private Point? anchor;
    private BoardSelection? selectionDrag;
    private ImmutableArray<BoardItem> selectionPreview = [];
    private readonly SmartInkGroup smartInk;
    private readonly InkTap inkTap;
    private readonly CanvasClick canvasClick = new();
    private readonly CanvasEraser eraser;
    private Shape? pendingArc;
    private ArcGesture? arcGesture;
    private BoardItem? boardGesture;
    private Point? panStart;
    private Vector panOffset;
    private double zoom = 1;
    private ItemVisibility newVisibility = ItemVisibility.Both;
    private bool newAnswer;
    private bool updating;
    private bool ready;

    public TeacherWindow() : this(ReleaseCatalog.CheckAsync) { }

    internal TeacherWindow(Func<CancellationToken, Task<AvailableUpdate?>> checkRelease)
    {
        this.checkRelease = checkRelease;
        saved = session.Lesson;
        InitializeComponent();
        InitializeMathTools();
        SubjectChoice.ItemsSource = Enum.GetValues<BoardSubject>();
        recordingClock = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => RefreshRecordingStatus(), Dispatcher);
        recordingClock.Stop();
        BuildVersion.Text = $"LOCAL LESSON   ·   v{ReleaseCatalog.InstalledVersion}";
        smartInk = new SmartInkGroup(() => session.Page.Items, items =>
        {
            session.ChangePage(session.Page with { Items = items });
            Refresh();
        });
        inkTap = new InkTap(CommitInk);
        eraser = new CanvasEraser(BoardInput, BoardFeedback, EraserSize, () => SelectionUnit,
            () => session.Page.Items, PreviewErase, CommitErase);
        eraser.Attach();
        DrawingInput.NewStroke = NewInk;
        DrawingInput.StrokeStarted += smartInk.Pause;
        DrawingInput.PreviewChanged += stroke =>
        {
            liveStroke = stroke;
            if (stroke is null)
                smartInk.Resume();
            RefreshLiveInk();
        };
        Width = Math.Clamp(SystemParameters.WorkArea.Width - 24, MinWidth, Width);
        Height = Math.Clamp(SystemParameters.WorkArea.Height - 24, MinHeight, Height);
        Colors.ItemsSource = Palette;
        Colors.DisplayMemberPath = nameof(ColorChoice.Name);
        Colors.SelectedIndex = 0;
        Widths.ItemsSource = new[] { 2, 4, 7, 12, 20 };
        Widths.SelectedIndex = 1;
        Paper.ItemsSource = Enum.GetValues<BoardPaper>();
        VisibilityChoice.ItemsSource = new VisibilityOption[]
        {
            new(ItemVisibility.Both, "Both"), new(ItemVisibility.TeacherOnly, "Teacher only"),
            new(ItemVisibility.StudentOnly, "Student only"), new(ItemVisibility.Hidden, "Hidden")
        };
        VisibilityChoice.DisplayMemberPath = nameof(VisibilityOption.Label);
        VisibilityChoice.SelectedValuePath = nameof(VisibilityOption.Value);
        VisibilityChoice.SelectedIndex = 0;
        BoardScroller.SizeChanged += (_, _) => SizeBoard();
        BoardScroller.ScrollChanged += (_, args) =>
        {
            if (Math.Abs(args.ViewportWidthChange) > .1 || Math.Abs(args.ViewportHeightChange) > .1)
                SizeBoard();
        };
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            source.AddHook(DisplayMessages);
            RegisterBoardShortcut(source.Handle);
        };
        LocationChanged += (_, _) => { if (ready) RefreshDisplays(); };
        Loaded += (_, _) =>
        {
            ready = true;
            RefreshDisplays();
            SetTool(Tool.Pen);
            SizeBoard();
            Refresh();
            Report("Ready. Connect an extended display or choose windowed output.");
            _ = RefreshUpdatesAsync(interactive: false);
        };
        PreviewKeyDown += Shortcuts;
        Closing += (_, args) =>
        {
            if (applyingUpdate)
            {
                studio?.Shutdown();
                return;
            }
            if (closeAfterRecording)
            {
                args.Cancel = recording is not null;
                if (!args.Cancel)
                    studio?.Shutdown();
                return;
            }
            if (!ConfirmDiscard())
            {
                args.Cancel = true;
                return;
            }
            if (recording is null)
            {
                studio?.Shutdown();
                return;
            }
            args.Cancel = true;
            closeAfterRecording = true;
            StopRecording();
        };
        Closed += (_, _) =>
        {
            updateLifetime.Cancel();
            updateLifetime.Dispose();
            UnregisterBoardShortcut();
            liveBroadcast?.Shutdown();
            DrawingInput.CancelStroke();
            sourceWindow?.Close();
            output?.Close();
            broadcastOutput?.Close();
        };
    }

    private BoardItem? Selected => selectedId is Guid id
        ? session.Page.Items.FirstOrDefault(item => item.Id == id) : null;

    private BoardItem? VisibleSelection => Selected is { Visibility: ItemVisibility.Both or ItemVisibility.TeacherOnly } item
        ? item : null;

    private ImmutableArray<BoardItem> CanvasSelection => !groupIds.IsEmpty
        ? session.Page.Items.Where(item => groupIds.Contains(item.Id) &&
            item.Visibility is ItemVisibility.Both or ItemVisibility.TeacherOnly).ToImmutableArray()
        : VisibleSelection is { } item ? [item] : [];

    private double SelectionUnit => BoardSelection.Unit(BoardSlot.Width);

    private uint InkColor
    {
        get
        {
            var choice = (ColorChoice?)Colors.SelectedItem ?? Palette[0];
            return choice == Palette[0] && BoardSurface.DarkPaper(session.Page.Paper, session.Page.Background)
                ? Palette[^1].Argb : choice.Argb;
        }
    }

    private double InkWidth => Widths.SelectedItem is int width ? width : 4;
    private double ToolWidth => tool == Tool.Highlighter ? Math.Max(12, InkWidth) : InkWidth;

    private void Report(string message) => Status.Text = message;

    private async void CheckForUpdates(object sender, RoutedEventArgs args)
    {
        if (checkingUpdate)
            return;
        if (availableUpdate is not { } update)
        {
            await RefreshUpdatesAsync(interactive: true);
            return;
        }

        if (recording is not null || liveBroadcast?.IsRunning == true)
        {
            Report("Stop recording and streaming before updating EduCast.");
            return;
        }

        var cancellationToken = updateLifetime.Token;
        checkingUpdate = true;
        UpdateButton.Content = "Downloading update…";
        UpdateButton.IsEnabled = false;
        Report($"Downloading and verifying EduCast v{update.Version}…");
        try
        {
            using var staged = await InAppUpdate.StageAsync(update, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;
            if (recording is not null || liveBroadcast?.IsRunning == true)
            {
                Report("Stop recording and streaming before updating EduCast.");
                return;
            }
            if (!PrepareLessonForUpdate())
            {
                Report("Update canceled.");
                return;
            }
            InAppUpdate.StartRunner(staged);
            applyingUpdate = true;
            Close();
        }
        catch (Exception error) when (FileError(error) || error is Win32Exception or HttpRequestException or OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                App.Log(error);
                Report(error is InvalidDataException ? error.Message : "The update could not start. Try again when connected.");
            }
        }
        finally
        {
            checkingUpdate = false;
            if (!cancellationToken.IsCancellationRequested && !applyingUpdate)
            {
                UpdateButton.Content = $"Update to v{update.Version}";
                UpdateButton.IsEnabled = true;
            }
        }
    }

    private bool PrepareLessonForUpdate()
    {
        CommitFields();
        if (session.Lesson == saved)
            return true;
        var answer = MessageBox.Show(this, "Save your lesson before EduCast restarts to install the update?",
            "Save before updating", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && Save(false));
    }

    private async Task RefreshUpdatesAsync(bool interactive)
    {
        if (checkingUpdate)
            return;
        checkingUpdate = true;
        var cancellationToken = updateLifetime.Token;
        UpdateButton.IsEnabled = false;
        if (interactive)
            Report("Checking GitHub for a newer release…");

        try
        {
            var update = await checkRelease(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;
            availableUpdate = update;
            if (update is not null)
            {
                UpdateButton.Content = $"Update to v{update.Version}";
                UpdateButton.Style = (Style)FindResource("Primary");
                UpdateButton.ToolTip = "Download, install for this user, and reopen EduCast";
            }
            else if (interactive)
                Report("No newer EduCast installer is published.");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (interactive && !cancellationToken.IsCancellationRequested)
                Report("Could not check GitHub for updates. Try again when connected.");
        }
        finally
        {
            checkingUpdate = false;
            if (!cancellationToken.IsCancellationRequested)
                UpdateButton.IsEnabled = true;
        }
    }

    private void Refresh()
    {
        var pageChanged = !ReferenceEquals(renderedPage, session.Page);
        if (pageChanged)
        {
            TeacherBoard.Edit(session.Page);
            renderedPage = session.Page;
        }
        var item = Selected;
        TeacherBoard.Mark(BoardSelection.BoundsOf(CanvasSelection), handleUnit: SelectionUnit);
        if (selectedId is not null && item is null)
            selectedId = null;

        updating = true;
        try
        {
            Title = $"EduCast Studio  ·  {session.Lesson.Title}" + (session.Lesson == saved ? "" : " *");
            if (!LessonTitle.IsKeyboardFocusWithin)
                LessonTitle.Text = session.Lesson.Title;
            if (!Notes.IsKeyboardFocusWithin)
                Notes.Text = session.Page.Notes;
            if (Pages.Items.Count != session.Lesson.Pages.Length)
                Pages.ItemsSource = Enumerable.Range(1, session.Lesson.Pages.Length).Select(i => $"Page {i:00}").ToArray();
            Pages.SelectedIndex = session.PageIndex;
            Prev.IsEnabled = session.PageIndex > 0;
            Next.IsEnabled = session.PageIndex + 1 < session.Lesson.Pages.Length;
            Paper.SelectedItem = session.Page.Paper;
            RefreshSubjectTools();
            UndoButton.IsEnabled = session.CanUndo;
            RedoButton.IsEnabled = session.CanRedo;
            RevealButton.Content = session.Page.AnswersRevealed ? "Hide answers" : "Reveal answers";
            RevealButton.IsEnabled = session.Page.Items.Any(content => content.IsAnswer);
            if (pageChanged)
            {
                Objects.ItemsSource = session.Page.Items.Reverse().Select(content =>
                    new ObjectChoice(content.Id, $"{ObjectName(content)}  ·  {VisibilityName(content.Visibility)}" +
                        (content.IsAnswer ? "  ·  answer" : ""))).ToArray();
                Objects.DisplayMemberPath = nameof(ObjectChoice.Label);
            }
            Objects.SelectedItem = Objects.Items.Cast<ObjectChoice>().FirstOrDefault(choice => choice.Id == selectedId);
            VisibilityChoice.SelectedValue = item?.Visibility ?? newVisibility;
            Answer.IsChecked = item?.IsAnswer ?? newAnswer;
            EditObjectButton.IsEnabled = item is Equation or Graph or BoardText or ChemicalEquation or TeachingDiagram;
            DeleteButton.IsEnabled = item is not null || !CanvasSelection.IsEmpty;
        }
        finally { updating = false; }

        RefreshProgram();
        UpdateInk(null, null);
    }

    private static string ObjectName(BoardItem item) => item switch
    {
        Ink { Highlighter: true } => "Highlight",
        Ink => "Ink",
        Shape shape => shape.ShapeKind.ToString(),
        BoardText => "Text",
        ChemicalEquation => "Chemical equation",
        TeachingDiagram diagram => DiagramLabel(diagram.Diagram),
        _ => item.GetType().Name
    };

    private static string VisibilityName(ItemVisibility visibility) => visibility switch
    {
        ItemVisibility.TeacherOnly => "teacher only",
        ItemVisibility.StudentOnly => "student only",
        _ => visibility.ToString().ToLowerInvariant()
    };

    private void RefreshProgram()
    {
        RefreshProgramBoard();
        RefreshLiveInk();
        RefreshStudioLayers();
        ProgramState.Text = session.Blackout ? "BLACKOUT" : session.Frozen ? "FROZEN" :
            session.Live ? "LIVE UPDATES" : "MANUAL TAKE";
        sourceWindow?.DisplayProgramState(ProgramState.Text, session.Frozen, session.Blackout);
        LiveMode.IsChecked = session.Live;
        FreezeButton.Content = session.Frozen ? "Resume" : "Freeze";
        BlackButton.Content = session.Blackout ? "Restore" : "Blackout";
        StartButton.Content = output is null ? "Start display" : "Move display";
        StopButton.IsEnabled = output is not null;
    }

    private void SetBoardGesture(BoardItem? item)
    {
        boardGesture = item;
        RefreshProgramBoard();
    }

    private void RefreshProgramBoard()
    {
        var frame = !selectionPreview.IsEmpty ? session.ProjectMoves(selectionPreview)
            : session.ProjectGesture(!mathOnSource && instrumentTrace is Shape arc ? arc : boardGesture);
        if (!ReferenceEquals(renderedOutput, frame))
        {
            renderedOutput = frame;
            ProgramPreview.Present(frame);
            output?.SetFrame(frame);
            broadcastOutput?.SetFrame(frame);
        }
    }

    private void RefreshLiveInk()
    {
        var trace = !mathOnSource && mathDrag?.Calibration != true ? instrumentTrace as Ink : null;
        var projected = session.ProjectLiveInk(trace ?? liveStroke);
        programInk = projected;
        if (projected is null && !liveInkShown)
            return;
        liveInkShown = projected is not null;
        ProgramPreview.PresentInk(projected);
        output?.SetLiveInk(projected);
        broadcastOutput?.SetLiveInk(projected);
    }

    private void AddPage(object sender, RoutedEventArgs args)
        => AddOrDuplicatePage(duplicate: false);

    private void DuplicatePage(object sender, RoutedEventArgs args)
        => AddOrDuplicatePage(duplicate: true);

    private void AddOrDuplicatePage(bool duplicate)
    {
        if (session.Lesson.Pages.Length >= 200)
        {
            Report("This lesson already has 200 pages.");
            return;
        }
        CancelGesture();
        ClearSelection();
        CommitFields();
        if (duplicate)
            session.DuplicatePage();
        else
            session.AddPage();
        Notes.Text = session.Page.Notes;
        Refresh();
    }

    private void PreviousPage(object sender, RoutedEventArgs args) => Navigate(session.PageIndex - 1);
    private void NextPage(object sender, RoutedEventArgs args) => Navigate(session.PageIndex + 1);

    private void SelectPage(object sender, SelectionChangedEventArgs args)
    {
        if (ready && !updating)
            Navigate(Pages.SelectedIndex);
    }

    private void Navigate(int index)
    {
        if (index < 0 || index >= session.Lesson.Pages.Length || index == session.PageIndex)
            return;
        CancelGesture();
        ClearSelection();
        CommitFields();
        session.GoTo(index);
        Notes.Text = session.Page.Notes;
        Refresh();
    }

    private void ClearPage(object sender, RoutedEventArgs args)
    {
        if (session.Page.Items.IsEmpty)
            return;
        if (MessageBox.Show(this, "Remove all objects on this page? You can undo this.", "Clear board",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        CancelGesture();
        ClearSelection();
        session.ChangePage(session.Page with { Items = [], AnswersRevealed = false });
        Refresh();
    }

    private void ChangePaper(object sender, SelectionChangedEventArgs args)
    {
        if (!ready || updating || Paper.SelectedItem is not BoardPaper paper || paper == session.Page.Paper)
            return;
        session.ChangePage(session.Page with { Paper = paper });
        Refresh();
    }

    private void RevealAnswers(object sender, RoutedEventArgs args)
    {
        session.ChangePage(session.Page with { AnswersRevealed = !session.Page.AnswersRevealed });
        Refresh();
    }

    private void Undo(object sender, RoutedEventArgs args)
    {
        CancelGesture();
        session.Undo();
        Refresh();
    }

    private void Redo(object sender, RoutedEventArgs args)
    {
        CancelGesture();
        session.Redo();
        Refresh();
    }

    private void EditTitle(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (!ready || updating)
            return;
        var title = LessonTitle.Text.Trim();
        if (title.Length == 0)
            title = "Untitled lesson";
        session.Rename(title);
        Refresh();
    }

    private void EditNotes(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (!ready || updating || session.Page.Notes == Notes.Text)
            return;
        session.ChangePage(session.Page with { Notes = Notes.Text });
        Refresh();
    }

    private void CommitFields()
    {
        var title = LessonTitle.Text.Trim();
        session.Rename(title.Length == 0 ? "Untitled lesson" : title);
        if (session.Page.Notes != Notes.Text)
            session.ChangePage(session.Page with { Notes = Notes.Text });
    }

    private bool ConfirmDiscard()
    {
        CommitFields();
        if (session.Lesson == saved)
            return true;
        return MessageBox.Show(this, "Close this lesson without saving your changes?", "Unsaved lesson",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void NewLesson(object sender, RoutedEventArgs args)
    {
        if (ConfirmDiscard())
            Load(Lesson.Empty(), null);
    }

    private void OpenLesson(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog { Filter = TeachingFile.Filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            OpenSelectedFile(dialog.FileName);
    }

    internal void OpenSelectedFile(string path)
    {
        var kind = TeachingFile.Classify(path);
        if (kind != TeachingFileKind.Board)
        {
            var source = ShowSourceWindow();
            switch (kind)
            {
                case TeachingFileKind.Browser: source.OpenFile(path); break;
                case TeachingFileKind.Media: source.OpenMediaFile(path); break;
                case TeachingFileKind.DesktopApp:
                    if (!source.OpenDesktopFile(path))
                        return;
                    break;
            }
            SetSourceStage(true);
            return;
        }
        if (!ConfirmDiscard())
            return;
        try { Load(LessonFile.Load(path), path); }
        catch (Exception error) when (FileError(error)) { ShowFileError(error); }
    }

    private void Load(Lesson lesson, string? path)
    {
        StopOutput(this, new RoutedEventArgs());
        studio?.Unpublish();
        sourceWindow?.Close();
        CloseMathTools();
        frozenLayers = null;
        CancelGesture();
        ClearSelection();
        session.Reset(lesson);
        projectPath = path;
        saved = lesson;
        LessonTitle.Text = lesson.Title;
        Notes.Text = session.Page.Notes;
        Refresh();
        Report($"Opened {lesson.Title}.");
    }

    private void SaveLesson(object sender, RoutedEventArgs args) => Save(false);
    private void SaveAs(object sender, RoutedEventArgs args) => Save(true);

    private bool Save(bool newPath)
    {
        CommitFields();
        var path = newPath ? null : projectPath;
        if (path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "EduCast lesson (*.educast)|*.educast", DefaultExt = ".educast",
                FileName = "Lesson.educast", AddExtension = true
            };
            if (dialog.ShowDialog(this) != true)
                return false;
            path = dialog.FileName;
        }
        try
        {
            LessonFile.Save(path, session.Lesson);
            saved = session.Lesson;
            projectPath = path;
            Refresh();
            Report($"Saved {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception error) when (FileError(error))
        {
            ShowFileError(error);
            return false;
        }
    }

    private static bool FileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException;

    private void ShowFileError(Exception error)
    {
        App.Log(error);
        MessageBox.Show(this, error.Message, "EduCast file operation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Sample(object sender, RoutedEventArgs args)
    {
        if (!ConfirmDiscard())
            return;
        const uint ink = 0xff172c39;
        const uint accent = 0xff145ac1;
        const uint answer = 0xff117969;
        var page = new Page("Suggested CSEC practice. Ask the class to factor the quadratic, then reveal the roots.",
            BoardPaper.Grid, false,
            [
                new BoardText(Guid.NewGuid(), ItemVisibility.Both, false, ink, new(70, 60), 52, "Quadratic functions"),
                new BoardText(Guid.NewGuid(), ItemVisibility.Both, false, ink, new(72, 146), 30, "Factor the quadratic and locate its roots."),
                new Equation(Guid.NewGuid(), ItemVisibility.Both, false, accent, new(72, 238), 52, "x^2 - 4x + 3 = 0"),
                new Graph(Guid.NewGuid(), ItemVisibility.Both, false, accent, new(790, 154), "y = x^2 - 4x + 3"),
                new Equation(Guid.NewGuid(), ItemVisibility.Both, true, answer, new(72, 400), 48, "(x - 1)(x - 3) = 0"),
                new BoardText(Guid.NewGuid(), ItemVisibility.Both, true, answer, new(72, 495), 30, "The roots are 1 and 3.")
            ]);
        Load(new Lesson(1, "Quadratics sample", [page, Page.Empty()]), null);
    }

    private void SelectObject(object sender, SelectionChangedEventArgs args)
    {
        if (!ready || updating)
            return;
        CancelGesture();
        groupIds = [];
        selectedId = (Objects.SelectedItem as ObjectChoice)?.Id;
        if (VisibleSelection is not null && tool != Tool.Select)
            SetTool(Tool.Select);
        Refresh();
    }

    private void ClearSelection()
    {
        selectedId = null;
        groupIds = [];
    }

    private void ChangeVisibility(object sender, SelectionChangedEventArgs args)
    {
        if (!ready || updating || VisibilityChoice.SelectedValue is not ItemVisibility visibility)
            return;
        if (Selected is { } item)
            Replace(item with { Visibility = visibility });
        else
            newVisibility = visibility;
    }

    private void ChangeAnswer(object sender, RoutedEventArgs args)
    {
        if (Selected is { } item)
            Replace(item with { IsAnswer = Answer.IsChecked == true });
        else
            newAnswer = Answer.IsChecked == true;
    }

    private void Replace(BoardItem replacement)
    {
        var item = Selected;
        if (item is null)
            return;
        if (item != replacement)
            session.ChangePage(session.Page with { Items = session.Page.Items.Replace(item, replacement) });
        Refresh();
    }

    private void DeleteSelected(object sender, RoutedEventArgs args)
    {
        ImmutableArray<BoardItem> selected = Selected is { } item ? [item] : CanvasSelection;
        if (selected.IsEmpty)
            return;
        session.ChangePage(session.Page with { Items = session.Page.Items.RemoveRange(selected) });
        ClearSelection();
        Refresh();
    }

    private void AddItem(BoardItem item) => AddItems([item]);

    private void AddItems(ImmutableArray<BoardItem> items)
    {
        if (items.IsEmpty)
            return;
        if (session.Page.Items.Length + items.Length > 3000)
        {
            Report("This page already has 3,000 objects. Add a page to continue.");
            return;
        }
        session.ChangePage(session.Page with { Items = session.Page.Items.AddRange(items) });
        Refresh();
    }

    private void InsertEquation(object sender, RoutedEventArgs args)
    {
        if (session.Page.Subject == BoardSubject.Chemistry)
            Prepare(InsertKind.Chemistry, "2H2(g) + O2(g) -> 2H2O(l)");
        else
            Prepare(InsertKind.Equation, session.Page.Subject == BoardSubject.Physics ? "F = m a" : "x^2 + 5x + 6 = 0");
    }
    private void InsertGraph(object sender, RoutedEventArgs args) => Prepare(InsertKind.Graph, "y = x^2 - 4x + 3");
    private void InsertText(object sender, RoutedEventArgs args) => Prepare(InsertKind.Text, "");

    private void Prepare(InsertKind kind, string initial)
    {
        CancelGesture();
        ClearSelection();
        Refresh();
        var dialog = new ObjectDialog(this, kind, initial, false);
        if (dialog.ShowDialog() != true)
            return;
        BeginPlacement(origin => ObjectDialog.Create(kind, dialog.Source, origin, InkColor, newVisibility, newAnswer));
    }

    private void BeginPlacement(Func<BoardPoint, BoardItem> create)
    {
        CancelGesture();
        ClearSelection();
        toolBeforePlacement = tool;
        placement = create;
        SetTool(Tool.Place);
        Refresh();
    }

    private void EditSelected(object sender, RoutedEventArgs args)
    {
        var item = Selected;
        if (item is TeachingDiagram diagram)
        {
            EditDiagram(diagram);
            return;
        }
        var kind = item switch
        {
            Equation => InsertKind.Equation, Graph => InsertKind.Graph,
            ChemicalEquation => InsertKind.Chemistry, _ => InsertKind.Text
        };
        var source = item switch
        {
            Equation equation => equation.Source, Graph graph => graph.Source,
            BoardText text => text.Text, ChemicalEquation chemical => chemical.Source, _ => null
        };
        if (source is null)
            return;
        var dialog = new ObjectDialog(this, kind, source, true);
        if (dialog.ShowDialog() != true)
            return;
        if (item is Equation equationItem)
            Replace(equationItem with { Source = dialog.Source });
        if (item is Graph graphItem)
            Replace(graphItem with { Source = dialog.Source });
        if (item is BoardText textItem)
            Replace(textItem with { Text = dialog.Source });
        if (item is ChemicalEquation chemicalItem)
            Replace(chemicalItem with { Source = dialog.Source });
    }

    private void ChooseTool(object sender, RoutedEventArgs args)
    {
        var choice = Enum.Parse<Tool>((string)((Button)sender).Tag);
        CancelGesture();
        if (choice != Tool.Select)
            ClearSelection();
        SetTool(choice);
        Refresh();
    }

    private void SetTool(Tool choice)
    {
        tool = choice;
        DrawingInput.EditingMode = IsInkTool(choice)
            ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        DrawingInput.EditingModeInverted = DrawingInput.EditingMode;
        DrawingInput.UseCustomCursor = true;
        DrawingInput.Cursor = choice == Tool.Select ? Cursors.Arrow : Cursors.Cross;
        EraserSize.Visibility = choice == Tool.Eraser ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        eraser.Activate(choice == Tool.Eraser);
        foreach (var button in new[] { PenButton, SquarePenButton, MarkerButton, EraseButton, ArrowButton, PointerButton,
            SelectButton, LineButton, CircleButton, ArcButton, RectButton, SmartInkButton, SmartMathButton })
            button.SetResourceReference(Control.BackgroundProperty, (string)button.Tag == choice.ToString() ? "AccentDark" : "Control");
        ToolHint.Text = choice switch
        {
            Tool.Pen => "Round pen · mouse, touch or stylus",
            Tool.SquarePen => "Square pen · mouse, touch or stylus",
            Tool.Highlighter => "Chisel highlighter · draw to emphasize",
            Tool.Eraser => "Eraser · choose a size and drag across writing",
            Tool.Arrow => "Draw arrow · drag from tail to tip. Hold Shift to snap the angle.",
            Tool.Pointer => "Arrow pointer · move over the board to point without leaving a mark",
            Tool.Select => "Double-click an object or drag a box around a group. Then drag to move or resize.",
            Tool.Line => "Ruler line · drag endpoints, Shift snaps to 15°",
            Tool.Circle => "Compass circle · drag from the center",
            Tool.Arc => "Arc · drag out the radius, sweep, then click to finish",
            Tool.Rectangle => "Rectangle · drag opposite corners",
            Tool.Ellipse => "Ellipse · drag opposite corners of its bounds",
            Tool.Triangle => "Triangle · drag opposite corners of its bounds",
            Tool.Polygon => $"{polygonSides}-sided polygon · drag opposite corners of its bounds",
            Tool.Cone or Tool.Sphere or Tool.Cube or Tool.Cuboid or Tool.Cylinder =>
                $"{choice} · drag opposite corners of its bounds",
            Tool.Resistor or Tool.Capacitor or Tool.Equal => "Symbol · drag from one end to the other",
            Tool.DoubleBond or Tool.TripleBond => "Bond · drag between two atom labels",
            Tool.SmartInk => "Sketch a shape and pause to clean it up. Undo restores the original strokes.",
            Tool.SmartMath => "Write a math expression and pause to typeset it. Undo restores the original strokes.",
            Tool.Instrument when instrumentDisplay?.Gauge is not null =>
                "Drag the body to move it. Drag or scroll over the carriage or thimble to adjust the reading.",
            Tool.Instrument => "Drag the instrument to move it. Use the handles to adjust it, or enable Trace to draw along it.",
            _ => "Click the board to place your object. Esc cancels."
        };
        UpdateInk(null, null);
    }

    private void UpdateInk(object? sender, RoutedEventArgs? args)
    {
        if (!ready)
            return;
        DrawingInput.DefaultDrawingAttributes = BoardSurface.InkAttributes(InkColor, ToolWidth,
            SmoothInk.IsChecked == true, tool == Tool.Highlighter, tool switch
            {
                Tool.SquarePen => InkTip.Square, Tool.Highlighter => InkTip.Chisel, _ => InkTip.Round
            });
    }

    private void CollectInk(object sender, InkCanvasStrokeCollectedEventArgs args)
    {
        if (DrawingInput.FinishStroke(args.Stroke) is not { Points.IsEmpty: false } ink ||
            inkTap.Collect(ink, SelectionUnit) is not { } hit)
            return;
        SelectCanvasTarget(hit);
    }

    internal CanvasTarget? TargetAt(IEnumerable<BoardItem> items, Point position, bool source)
    {
        if (OverClock(position))
            return new(CanvasTargetKind.Clock);
        if (InstrumentAt(Pointer(position), source) is { } instrument)
            return new(CanvasTargetKind.Instrument, instrument.Id);
        return BoardSelection.Hit(items, position) is { } item ? new(CanvasTargetKind.Content, item.Id) : null;
    }

    private void SelectCanvasTarget(CanvasTarget hit)
    {
        CancelGesture();
        ClearSelection();
        if (hit.Kind == CanvasTargetKind.Instrument && SelectInstrument(hit.Id, source: false))
            SetTool(Tool.Instrument);
        else
        {
            selectedId = hit.Kind == CanvasTargetKind.Content ? hit.Id : null;
            SetTool(Tool.Select);
        }
        Refresh();
    }

    internal bool TrySelectAt(Point position, int clickCount)
    {
        var target = TargetAt(session.Page.Items, position, source: false);
        if (!canvasClick.Begin(clickCount, target, position) || target is not { } hit)
            return false;
        SelectCanvasTarget(hit);
        return true;
    }

    private void CommitInk(Ink ink)
    {
        if (tool is Tool.SmartInk or Tool.SmartMath)
            CollectSketch(ink);
        else
            AddItem(ink);
    }

    private static bool IsInkTool(Tool choice) =>
        choice is Tool.Pen or Tool.SquarePen or Tool.Highlighter or Tool.SmartInk or Tool.SmartMath;

    private Ink NewInk(DrawingAttributes attributes) => LiveInkCanvas.Describe(attributes, newVisibility, newAnswer);

    private static BoardPoint PointOf(Point point) => new(
        Math.Clamp(point.X, 0, BoardSurface.BoardWidth), Math.Clamp(point.Y, 0, BoardSurface.BoardHeight));

    private void BoardDown(object sender, MouseButtonEventArgs args)
    {
        var position = args.GetPosition(BoardInput);
        SetMathSurface(source: false);
        if (tool != Tool.Eraser && TeacherInstrument.CloseAt(position))
        {
            args.Handled = true;
            return;
        }
        if (tool == Tool.Select && BoardSelection.CloseAt(BoardSelection.BoundsOf(CanvasSelection), position, SelectionUnit))
        {
            CancelGesture();
            DeleteSelected(sender, args);
            args.Handled = true;
            return;
        }
        if (!IsInkTool(tool) && TrySelectAt(position, args.ClickCount))
        {
            args.Handled = true;
            return;
        }
        if (IsInkTool(tool))
        {
            var mouse = args.StylusDevice is null;
            var target = mouse ? TargetAt(session.Page.Items, position, source: false) : null;
            inkTap.Begin(args.ClickCount, target, mouse);
            return;
        }
        if (tool == Tool.Eraser)
            return;
        if (tool == Tool.Select && BeginClockDrag(position, source: false) ||
            tool == Tool.Instrument && BeginMathDrag(Pointer(position), source: false))
        {
            args.Handled = true;
            if (ClockDragging(source: false))
            {
                ClearSelection();
                Refresh();
            }
            if (!BoardInput.CaptureMouse())
                CancelGesture();
            return;
        }
        if (tool == Tool.Instrument)
        {
            args.Handled = true;
            return;
        }
        args.Handled = true;
        if (tool == Tool.Pointer)
            return;
        if (tool == Tool.Place && placement is { } value)
        {
            canvasClick.Single(() =>
            {
                placement = null;
                SetTool(toolBeforePlacement);
                AddItem(value(PointOf(position)));
            });
            return;
        }
        if (tool == Tool.Select)
        {
            if (BoardInput.CaptureMouse())
            {
                anchor = position;
                selectionDrag = BoardSelection.BeginSelected(CanvasSelection, position, SelectionUnit);
                if (selectionDrag is { } drag)
                    DrawingInput.Cursor = drag.Cursor;
                else
                {
                    ClearSelection();
                    Refresh();
                }
            }
            return;
        }
        if (tool == Tool.Arc && pendingArc is { } arc && arcGesture is { } sweep)
        {
            sweep.Follow(PointOf(position));
            var completed = sweep.Apply(arc);
            canvasClick.Single(() =>
            {
                (pendingArc, arcGesture, boardGesture) = (null, null, null);
                if (Math.Abs(completed.Sweep) >= 1)
                    AddItem(completed);
                Refresh();
                ToolHint.Text = "Arc · drag out the radius, sweep, then click to finish";
            });
            return;
        }

        if (BoardInput.CaptureMouse())
            anchor = position;
    }

    private void BoardMove(object sender, MouseEventArgs args)
    {
        var position = args.GetPosition(BoardInput);
        if (args.LeftButton == MouseButtonState.Pressed)
            canvasClick.Move(position, SelectionUnit);
        if (tool == Tool.Eraser)
            return;
        if (ClockDragging(source: false) && BoardInput.IsMouseCaptured)
        {
            MoveClock(position);
            return;
        }
        if (tool == Tool.Pointer)
        {
            SetBoardPointer(position);
            return;
        }
        if (InstrumentDragging(source: false) && BoardInput.IsMouseCaptured)
        {
            UpdateMathDrag(Pointer(position), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }
        if (pendingArc is { } arc)
        {
            arcGesture!.Follow(PointOf(position));
            var preview = arcGesture.Apply(arc);
            TeacherBoard.Mark(null, preview);
            SetBoardGesture(Math.Abs(preview.Sweep) >= 1 ? preview : null);
            return;
        }
        if (tool == Tool.Select)
        {
            PreviewSelection(position);
            return;
        }
        if (anchor is not Point start || !BoardInput.IsMouseCaptured)
            return;
        var shape = MakeShape(start, position);
        TeacherBoard.Mark(null, tool == Tool.Arc ? shape with { ShapeKind = ShapeKind.Circle } : shape);
        if (tool != Tool.Arc)
            SetBoardGesture(shape);
    }

    private void PreviewSelection(Point position)
    {
        if (anchor is not Point start || !BoardInput.IsMouseCaptured)
        {
            DrawingInput.Cursor = BoardSelection.Hover(BoardSelection.BoundsOf(CanvasSelection), position, SelectionUnit);
            return;
        }
        if (selectionDrag is not { } drag)
        {
            BoardFeedback.ShowMarquee(new Rect(start, position), SelectionUnit);
            return;
        }
        var moved = drag.CommitAll(position);
        if (selectionPreview.IsEmpty && moved == drag.Items)
            return;
        selectionPreview = moved == drag.Items ? [] : moved;
        TeacherBoard.Edit(session.Page with { Items = BoardSelection.Apply(session.Page.Items, selectionPreview) });
        renderedPage = null;
        TeacherBoard.Mark(BoardSelection.BoundsOf(moved), handleUnit: SelectionUnit);
        RefreshProgramBoard();
    }

    private void BoardUp(object sender, MouseButtonEventArgs args)
    {
        if (tool == Tool.Eraser)
            return;
        if (ClockDragging(source: false))
        {
            MoveClock(args.GetPosition(BoardInput));
            FinishClockDrag();
            BoardInput.ReleaseMouseCapture();
            args.Handled = true;
            return;
        }
        if (InstrumentDragging(source: false))
        {
            args.Handled = true;
            EndMathDrag(Pointer(args.GetPosition(BoardInput)), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            BoardInput.ReleaseMouseCapture();
            return;
        }
        if (anchor is not Point start)
            return;
        args.Handled = true;
        var position = args.GetPosition(BoardInput);
        anchor = null;
        boardGesture = null;
        BoardInput.ReleaseMouseCapture();
        if (tool == Tool.Select)
        {
            var drag = selectionDrag;
            selectionDrag = null;
            selectionPreview = [];
            if (drag is not null)
            {
                var moved = drag.CommitAll(position);
                if (moved != drag.Items)
                    session.ChangePage(session.Page with { Items = BoardSelection.Apply(session.Page.Items, moved) });
            }
            else
            {
                BoardFeedback.ShowMarquee(Rect.Empty, SelectionUnit);
                var selected = BoardSelection.Within(session.Page.Items, start, position, SelectionUnit);
                selectedId = selected.Length == 1 ? selected[0].Id : null;
                groupIds = selected.Length > 1 ? selected.Select(item => item.Id).ToImmutableHashSet() : [];
            }
            Refresh();
            DrawingInput.Cursor = BoardSelection.Hover(BoardSelection.BoundsOf(CanvasSelection), position, SelectionUnit);
            return;
        }

        if (!CanvasClick.IsDrag(start, position, SelectionUnit))
        {
            Refresh();
            return;
        }
        canvasClick.Clear();
        var shape = MakeShape(start, position);
        if (tool == Tool.Arc)
        {
            pendingArc = shape;
            arcGesture = new ArcGesture(shape.Start, shape.End);
            TeacherBoard.Mark(null, shape with { ShapeKind = ShapeKind.Circle });
            ToolHint.Text = "Sweep around the pivot in either direction, then click to finish.";
            return;
        }
        AddItem(shape);
    }

    private Shape MakeShape(Point start, Point end)
    {
        if (tool is Tool.Line or Tool.Arrow && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var delta = end - start;
            var angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 12)) * (Math.PI / 12);
            end = start + new Vector(Math.Cos(angle) * delta.Length, Math.Sin(angle) * delta.Length);
        }
        var kind = tool switch
        {
            Tool.Circle => ShapeKind.Circle, Tool.Rectangle => ShapeKind.Rectangle, Tool.Arc => ShapeKind.Arc,
            Tool.Triangle => ShapeKind.Triangle, Tool.Ellipse => ShapeKind.Ellipse, Tool.Equal => ShapeKind.Equal,
            Tool.Resistor => ShapeKind.Resistor, Tool.Capacitor => ShapeKind.Capacitor,
            Tool.DoubleBond => ShapeKind.DoubleBond, Tool.TripleBond => ShapeKind.TripleBond,
            Tool.Polygon => ShapeKind.Polygon, Tool.Cone => ShapeKind.Cone, Tool.Sphere => ShapeKind.Sphere,
            Tool.Cube => ShapeKind.Cube, Tool.Cuboid => ShapeKind.Cuboid, Tool.Cylinder => ShapeKind.Cylinder,
            Tool.Arrow => ShapeKind.Arrow, _ => ShapeKind.Line
        };
        var first = PointOf(start);
        var last = PointOf(end);
        var shape = new Shape(Guid.NewGuid(), newVisibility, newAnswer, InkColor, InkWidth, kind, first, last);
        if (kind != ShapeKind.Polygon)
            return shape;
        var center = new BoardPoint((first.X + last.X) / 2, (first.Y + last.Y) / 2);
        return shape with
        {
            Vertices = Enumerable.Range(0, polygonSides).Select(i =>
            {
                var angle = i * Math.Tau / polygonSides - Math.PI / 2;
                return new BoardPoint(center.X + Math.Cos(angle) * Math.Abs(last.X - first.X) / 2,
                    center.Y + Math.Sin(angle) * Math.Abs(last.Y - first.Y) / 2);
            }).ToImmutableArray()
        };
    }

    private void PreviewErase()
    {
        TeacherBoard.Edit(session.Page with { Items = eraser.Draft ?? session.Page.Items });
        renderedPage = null;
    }

    private void CommitErase(ImmutableArray<BoardItem> next)
    {
        if (next.Length > 3000)
            Report("This page can hold up to 3,000 objects. Remove some objects before splitting more ink.");
        else if (next != session.Page.Items)
            session.ChangePage(session.Page with { Items = next });
        Refresh();
    }

    private void BoardCaptureLost(object sender, MouseEventArgs args)
    {
        if (anchor is not null || InstrumentDragging(source: false) || ClockDragging(source: false))
        {
            CancelGesture();
            Refresh();
        }
    }

    private void CancelGesture()
    {
        canvasClick.Clear();
        inkTap.Flush();
        smartInk.Complete();
        CancelClockDrag(source: false);
        SetBoardPointer(null);
        if (!mathOnSource)
            CancelMathDrag();
        DrawingInput.CancelStroke();
        eraser.Cancel();
        anchor = null;
        selectionDrag = null;
        selectionPreview = [];
        BoardFeedback.ShowMarquee(Rect.Empty, SelectionUnit);
        (pendingArc, arcGesture) = (null, null);
        SetBoardGesture(null);
        if (BoardInput.IsMouseCaptured)
            BoardInput.ReleaseMouseCapture();
        if (placement is not null)
        {
            placement = null;
            SetTool(toolBeforePlacement);
        }
        if (tool == Tool.Arc)
            ToolHint.Text = "Arc · drag out the radius, sweep, then click to finish";
        TeacherBoard.Mark(null);
    }

    private void ZoomOut(object sender, RoutedEventArgs args) => Zoom(zoom / 1.25);
    private void ZoomIn(object sender, RoutedEventArgs args) => Zoom(zoom * 1.25);
    private void FitBoard(object sender, RoutedEventArgs args) => Zoom(1);

    private void Zoom(double value)
    {
        zoom = Math.Clamp(value, 1, 4);
        SizeBoard();
    }

    private void SizeBoard()
    {
        if (!ready)
            return;
        var width = BoardScroller.ActualWidth - (zoom > 1 ? SystemParameters.VerticalScrollBarWidth : 0);
        var height = BoardScroller.ActualHeight - (zoom > 1 ? SystemParameters.HorizontalScrollBarHeight : 0);
        var fitted = Math.Max(0, Math.Min(width, height * 16 / 9));
        var boardWidth = Math.Floor(fitted * zoom);
        BoardSlot.Width = boardWidth;
        BoardSlot.Height = Math.Floor(boardWidth * 9 / 16);
        TeacherInstrument.ScaleControls(SelectionUnit);
        if (tool == Tool.Select && selectionDrag is null)
            TeacherBoard.Mark(BoardSelection.BoundsOf(CanvasSelection), handleUnit: SelectionUnit);
        ZoomButton.Content = zoom == 1 ? "Fit" : $"{zoom:P0}";
    }

    private void StartPan(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Middle || zoom <= 1 || Mouse.Captured is not null)
            return;
        if (!BoardScroller.CaptureMouse())
            return;
        panStart = args.GetPosition(BoardScroller);
        panOffset = new Vector(BoardScroller.HorizontalOffset, BoardScroller.VerticalOffset);
        BoardScroller.Cursor = Cursors.ScrollAll;
        args.Handled = true;
    }

    private void Pan(object sender, MouseEventArgs args)
    {
        if (panStart is not { } start)
            return;
        var delta = start - args.GetPosition(BoardScroller);
        BoardScroller.ScrollToHorizontalOffset(panOffset.X + delta.X);
        BoardScroller.ScrollToVerticalOffset(panOffset.Y + delta.Y);
        args.Handled = true;
    }

    private void StopPan(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Middle || panStart is null)
            return;
        BoardScroller.ReleaseMouseCapture();
        args.Handled = true;
    }

    private void PanCaptureLost(object sender, MouseEventArgs args)
    {
        panStart = null;
        BoardScroller.ClearValue(CursorProperty);
    }

    private void WheelZoom(object sender, MouseWheelEventArgs args)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            args.Handled = tool == Tool.Instrument && !mathOnSource && TryGaugeWheel(Pointer(args.GetPosition(BoardInput)), args.Delta,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }
        Zoom(args.Delta > 0 ? zoom * 1.25 : zoom / 1.25);
        args.Handled = true;
    }

    private IntPtr DisplayMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HotkeyMessage && wParam.ToInt32() == BoardHotkeyId)
        {
            ReturnToBoard();
            handled = true;
        }
        if (message is WindowsDisplays.DisplayChange or WindowsDisplays.DpiChange)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshDisplays));
        return IntPtr.Zero;
    }

    private void RefreshDisplays()
    {
        if (!ready)
            return;
        ApplyDisplays(WindowsDisplays.Get(), WindowsDisplays.For(this));
    }

    internal void ApplyDisplays(IReadOnlyList<Display> displays, string? teacher)
    {
        var options = displays.Where(display => display.Device != teacher).ToArray();
        var selection = DisplayChoice.SelectedItem is OutputChoice selected
            ? selected.Display?.Device : preferredDisplay;
        if (DisplayChoice.ItemsSource is not OutputChoice[] current ||
            !current.Skip(1).Select(choice => choice.Display).SequenceEqual(options))
        {
            var choices = new[] { new OutputChoice(null) }.Concat(options.Select(display => new OutputChoice(display))).ToArray();
            DisplayChoice.ItemsSource = choices;
            DisplayChoice.SelectedItem = choices.FirstOrDefault(choice => choice.Display?.Device == selection) ??
                (selection is null ? choices[0] : null);
        }
        if (activeDisplay is not { } active)
            return;
        var stillAvailable = options.FirstOrDefault(display => display.Device == active.Device);
        if (stillAvailable is null)
        {
            output?.Close();
            Report("Student display disconnected or became the teacher display. Select a display and restart output.");
            return;
        }
        if (stillAvailable == active || output is null)
            return;
        try
        {
            WindowsDisplays.Place(output, stillAvailable);
            activeDisplay = stillAvailable;
        }
        catch (Win32Exception error)
        {
            output.Close();
            App.Log(error);
            Report("Windows could not reposition the student display. Select it and restart output.");
        }
    }

    private void StartOutput(object sender, RoutedEventArgs args)
    {
        RefreshDisplays();
        if (DisplayChoice.SelectedItem is not OutputChoice choice)
        {
            Report("Select an available student display.");
            return;
        }
        if (output is not null && choice.Display == activeDisplay)
        {
            if (activeDisplay is null)
                output.Activate();
            return;
        }
        output?.Close();
        var window = new DisplayWindow(this, choice.Display) { FourThree = AspectChoice.SelectedIndex == 1 };
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(output, window))
                return;
            output = null;
            activeDisplay = null;
            RefreshProgram();
        };
        try
        {
            window.SetFrame(renderedOutput);
            window.SetLiveInk(programInk);
            window.SetLayers(visibleLayers);
            window.Show();
            output = window;
            activeDisplay = choice.Display;
            preferredDisplay = choice.Display?.Device;
            RefreshProgram();
            Report(choice.Display is null ? "Student output is open in a separate window." : $"Presenting on {choice.Label}.");
        }
        catch (Win32Exception error)
        {
            window.Close();
            App.Log(error);
            Report("Windows could not start output on that display.");
        }
    }

    private void StopOutput(object sender, RoutedEventArgs args)
    {
        if (output is null)
            return;
        output.Close();
        Report("Student display stopped.");
    }

    private void ChangeAspect(object sender, SelectionChangedEventArgs args)
    {
        if (output is not null)
            output.FourThree = AspectChoice.SelectedIndex == 1;
    }

    private void SetLive(object sender, RoutedEventArgs args)
    {
        if (session.Live)
        {
            TakeSource();
            takenClockOffset = clockOffset;
        }
        session.Live = LiveMode.IsChecked == true;
        if (session.Live)
            RefreshMathInstrument(publish: true);
        RefreshProgram();
    }

    private void Freeze(object sender, RoutedEventArgs args)
    {
        frozenLayers = session.Frozen ? null : LiveStudioLayers(includeGestures: false);
        if (!sourceOnStage && frozenLayers is { Source: not null } held)
            frozenLayers = held with { Source = null };
        session.ToggleFreeze();
        RefreshProgram();
    }

    private void Blackout(object sender, RoutedEventArgs args)
    {
        session.Blackout = !session.Blackout;
        RefreshProgram();
    }

    private void Take(object sender, RoutedEventArgs args)
    {
        session.Take();
        TakeSource();
        takenClockOffset = clockOffset;
        RefreshMathInstrument(publish: true);
        RefreshProgram();
    }

    private void ExportProgram(object sender, RoutedEventArgs args)
    {
        var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "EduCast program.png" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            AtomicFile.Write(dialog.FileName, stream => ProgramImage.WritePng(CurrentStudioFrame(), stream));
            Report("Exported the Program frame as a 1280 × 720 PNG.");
        }
        catch (Exception error) when (FileError(error)) { ShowFileError(error); }
    }

    private StudioFrame CurrentStudioFrame() => new(renderedOutput, programInk, visibleLayers);

    private void OpenStudio(object sender, RoutedEventArgs args) => ShowStudio();

    internal void ShowStudio()
    {
        if (studio is null)
        {
            studio = new StudioTools(this);
            studio.LayersChanged += ApplyStudioLayers;
            studio.RecordRequested += StartRecording;
            studio.StopRequested += StopRecording;
        }
        studio.Show();
        studio.Activate();
    }

    internal void ApplyStudioLayers(ProgramLayers layers)
    {
        studioLayers = layers;
        RefreshTeacherOverlays();
        RefreshStudioLayers();
    }

    private void RefreshStudioLayers()
    {
        var live = LiveStudioLayers();
        if (frozenLayers is { } held)
            frozenLayers = held with
            {
                CameraOne = live.CameraOne is null ? null : held.CameraOne,
                CameraTwo = live.CameraTwo is null ? null : held.CameraTwo,
                Clock = live.Clock is null ? null : held.Clock,
                Instruments = FilterHeldInstruments(held.Instruments)
            };
        var next = session.Blackout ? new ProgramLayers() : frozenLayers ?? live;
        if (!sourceOnStage && next.Source is not null)
            next = next with { Source = null };
        if (next == visibleLayers)
            return;
        visibleLayers = next;
        ProgramPreview.PresentLayers(next);
        output?.SetLayers(next);
        broadcastOutput?.SetLayers(next);
    }

    private ProgramLayers LiveStudioLayers(bool includeGestures = true) => studioLayers with
    {
        Instruments = CurrentInstruments(),
        ClockOffset = session.Live
            ? includeGestures ? clockOffset : clockDrag?.Original ?? clockOffset : takenClockOffset,
        Source = session.Live ? sourceWindow?.PublishedFrame(includeGestures) :
            sourceWindow?.AnimateTakenMedia(takenSource) ?? takenSource,
        Pointer = session.Live && includeGestures ? boardPointer : null
    };

    private void RecordingAction(object sender, RoutedEventArgs args)
    {
        if (recording is null)
            OpenStudio(sender, args);
        else
            StopRecording();
    }

    private void StartRecording(string executable, RecordingProfile profile, MicrophoneDevice? microphone)
    {
        if (recording is not null || liveBroadcast?.IsRunning == true)
        {
            Report("Stop the live stream before starting a recording.");
            studio?.Report("Stop the live stream before starting a recording.");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "MP4 video (*.mp4)|*.mp4", DefaultExt = ".mp4", AddExtension = true,
            FileName = $"EduCast {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4"
        };
        if (dialog.ShowDialog((Window?)studio ?? this) != true)
            return;
        try
        {
            var renderer = new ProgramVideoRenderer(profile);
            var job = new ProgramRecording(executable, dialog.FileName, profile, () =>
                Dispatcher.InvokeAsync(() => renderer.Capture(CurrentStudioFrame()), DispatcherPriority.Background).Task,
                microphone);
            recording = job;
            recordingClock.Start();
            studio?.SetRecording(active: true);
            studio?.Report(microphone is null ? "Recording the clean Program without audio."
                : $"Recording the clean Program with {microphone.Name}. Freeze and blackout affect video only.");
            Report(job.IncludesAudio ? "Recording started with microphone audio." : "Recording started without audio.");
            RefreshRecordingStatus();
            _ = ObserveRecordingAsync(job);
        }
        catch (Exception error) when (FileError(error) || error is Win32Exception)
        {
            studio?.SetRecording(active: false);
            studio?.Report(error.Message);
            Report("Could not start the recording.");
        }
    }

    private void StopRecording()
    {
        if (recording is null || stoppingRecording)
            return;
        stoppingRecording = true;
        recording.Stop();
        studio?.SetRecording(active: true, finishing: true);
        RefreshRecordingStatus();
    }

    private void RefreshRecordingStatus()
    {
        RecordingButton.Content = recording is null ? "Record…" : stoppingRecording ? "Finishing…" :
            $"● REC{(recording.IncludesAudio ? " + mic" : "")} {recording.Elapsed:hh\\:mm\\:ss} · Stop";
        RecordingButton.IsEnabled = !stoppingRecording;
        if (recording is null)
        {
            RecordingButton.ClearValue(Control.BackgroundProperty);
            RecordingButton.ClearValue(Control.ForegroundProperty);
        }
        else
        {
            RecordingButton.Background = Brushes.Firebrick;
            RecordingButton.Foreground = Brushes.White;
        }
    }

    private async Task ObserveRecordingAsync(ProgramRecording job)
    {
        try
        {
            var path = await job.Completion;
            studio?.Report($"Saved {path}.");
            Report($"Saved recording {Path.GetFileName(path)}.");
        }
        catch (Exception error)
        {
            App.Log(error);
            studio?.Report(error.Message);
            if (closeAfterRecording)
                MessageBox.Show(this, error.Message, "Recording could not finish", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                Report("Recording failed. Open Studio for details.");
        }
        finally
        {
            recording = null;
            stoppingRecording = false;
            recordingClock.Stop();
            studio?.SetRecording(active: false);
            RefreshRecordingStatus();
            if (closeAfterRecording)
                Close();
        }
    }

    private void Shortcuts(object sender, KeyEventArgs args)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        var modifiers = Keyboard.Modifiers;
        var editingText = Keyboard.FocusedElement is TextBox;
        RoutedEventHandler? action = (modifiers, key) switch
        {
            (ModifierKeys.Control, Key.N) => NewLesson,
            (ModifierKeys.Control, Key.O) => OpenLesson,
            (ModifierKeys.Control, Key.S) => SaveLesson,
            (ModifierKeys.Control | ModifierKeys.Shift, Key.S) => SaveAs,
            (ModifierKeys.Control | ModifierKeys.Shift, Key.B) when boardShortcutWindow == IntPtr.Zero => ShowBoard,
            (ModifierKeys.Control, Key.Z) when !editingText => Undo,
            (ModifierKeys.Control, Key.Y) when !editingText => Redo,
            (ModifierKeys.Alt, Key.Left) => PreviousPage,
            (ModifierKeys.Alt, Key.Right) => NextPage,
            (ModifierKeys.None, Key.Delete) when !editingText => DeleteSelected,
            (ModifierKeys.None, Key.F5) => StartOutput,
            (ModifierKeys.Shift, Key.F5) => StopOutput,
            (ModifierKeys.None, Key.F6) => Freeze,
            (ModifierKeys.None, Key.F7) => Blackout,
            (ModifierKeys.None, Key.F8) => Take,
            _ => null
        };
        if (key == Key.Escape)
        {
            CancelGesture();
            ClearSelection();
            SetTool(Tool.Select);
            Refresh();
            args.Handled = true;
        }
        if (action is not null)
        {
            action(this, args);
            args.Handled = true;
        }
    }
}
