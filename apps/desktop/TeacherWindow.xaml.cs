using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
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
    private enum Tool { Pen, Highlighter, Eraser, Select, Line, Circle, Arc, Rectangle, Place }
    private sealed record ColorChoice(string Name, uint Argb);
    private sealed record OutputChoice(Display? Display)
    {
        public string Label => Display?.Label ?? "Windowed output";
    }
    private sealed record ObjectChoice(Guid Id, string Label);
    private sealed record VisibilityOption(ItemVisibility Value, string Label);

    private static readonly ColorChoice[] Palette =
    [
        new("Ink", 0xff172c39), new("Blue", 0xff145ac1), new("Teal", 0xff117969),
        new("Red", 0xffb32f47), new("Amber", 0xffc06e15), new("Violet", 0xff8142bd),
        new("White", 0xfff5fbff)
    ];

    private readonly Session session = new();
    private readonly CancellationTokenSource updateLifetime = new();
    private readonly Func<CancellationToken, Task<AvailableUpdate?>> checkRelease;
    private AvailableUpdate? availableUpdate;
    private bool checkingUpdate;
    private Lesson saved;
    private Page? renderedPage;
    private ProgramFrame? renderedOutput;
    private Ink? liveStroke;
    private bool liveInkShown;
    private string? projectPath;
    private DisplayWindow? output;
    private Display? activeDisplay;
    private string? preferredDisplay;
    private Guid? selectedId;
    private Tool tool = Tool.Pen;
    private Tool toolBeforePlacement;
    private (InsertKind Kind, string Source)? placement;
    private Point? anchor;
    private bool movingSelection;
    private Shape? pendingArc;
    private HashSet<Guid>? erasing;
    private Point? previousErase;
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
        BuildVersion.Text = $"LOCAL LESSON   ·   v{ReleaseCatalog.InstalledVersion}";
        DrawingInput.NewStroke = NewInk;
        DrawingInput.PreviewChanged += stroke =>
        {
            liveStroke = stroke;
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
            ((HwndSource)PresentationSource.FromVisual(this)).AddHook(DisplayMessages);
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
        Closing += (_, args) => { if (!ConfirmDiscard()) args.Cancel = true; };
        Closed += (_, _) =>
        {
            updateLifetime.Cancel();
            updateLifetime.Dispose();
            DrawingInput.CancelStroke();
            output?.Close();
        };
    }

    private BoardItem? Selected => selectedId is Guid id
        ? session.Page.Items.FirstOrDefault(item => item.Id == id) : null;

    private uint InkColor
    {
        get
        {
            var choice = (ColorChoice?)Colors.SelectedItem ?? Palette[0];
            return choice == Palette[0] && session.Page.Paper == BoardPaper.Dark ? Palette[^1].Argb : choice.Argb;
        }
    }

    private double InkWidth => Widths.SelectedItem is int width ? width : 4;
    private double ToolWidth => tool == Tool.Highlighter ? Math.Max(12, InkWidth) : InkWidth;

    private void Report(string message) => Status.Text = message;

    private async void CheckForUpdates(object sender, RoutedEventArgs args)
    {
        if (availableUpdate is not { } update)
        {
            await RefreshUpdatesAsync(interactive: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(update.Installer.AbsoluteUri) { UseShellExecute = true });
            Report("The installer download opened in your browser. Save your lesson before running it.");
        }
        catch (Win32Exception error)
        {
            App.Log(error);
            Report("Could not open the download. Visit github.com/WealthGate/EduCast/releases.");
        }
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
                UpdateButton.Content = $"Download v{update.Version}";
                UpdateButton.Style = (Style)FindResource("Primary");
                UpdateButton.ToolTip = "Open the Windows installer download on GitHub";
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
        var visibleSelection = item is { Visibility: ItemVisibility.Both or ItemVisibility.TeacherOnly };
        TeacherBoard.Mark(visibleSelection ? item : null);
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
            EditObjectButton.IsEnabled = item is Equation or Graph or BoardText;
            DeleteButton.IsEnabled = item is not null;
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
        var frame = session.Output;
        if (!ReferenceEquals(renderedOutput, frame))
        {
            renderedOutput = frame;
            ProgramPreview.Present(frame);
            output?.SetFrame(frame);
        }
        RefreshLiveInk();
        ProgramState.Text = session.Blackout ? "BLACKOUT" : session.Frozen ? "FROZEN" :
            session.Live ? "LIVE UPDATES" : "MANUAL TAKE";
        LiveMode.IsChecked = session.Live;
        FreezeButton.Content = session.Frozen ? "Resume" : "Freeze";
        BlackButton.Content = session.Blackout ? "Restore" : "Blackout";
        StartButton.Content = output is null ? "Start display" : "Move display";
        StopButton.IsEnabled = output is not null;
    }

    private void RefreshLiveInk()
    {
        var projected = session.ProjectLiveInk(liveStroke);
        if (projected is null && !liveInkShown)
            return;
        liveInkShown = projected is not null;
        ProgramPreview.PresentInk(projected);
        output?.SetLiveInk(projected);
    }

    private void AddPage(object sender, RoutedEventArgs args)
    {
        if (session.Lesson.Pages.Length >= 200)
        {
            Report("This lesson already has 200 pages.");
            return;
        }
        CancelGesture();
        ClearSelection();
        CommitFields();
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
        var dialog = new OpenFileDialog { Filter = "EduCast lesson (*.educast)|*.educast", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || !ConfirmDiscard())
            return;
        try { Load(LessonFile.Load(dialog.FileName), dialog.FileName); }
        catch (Exception error) when (FileError(error)) { ShowFileError(error); }
    }

    private void Load(Lesson lesson, string? path)
    {
        StopOutput(this, new RoutedEventArgs());
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

    private void Save(bool newPath)
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
                return;
            path = dialog.FileName;
        }
        try
        {
            LessonFile.Save(path, session.Lesson);
            saved = session.Lesson;
            projectPath = path;
            Refresh();
            Report($"Saved {Path.GetFileName(path)}.");
        }
        catch (Exception error) when (FileError(error)) { ShowFileError(error); }
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
        selectedId = (Objects.SelectedItem as ObjectChoice)?.Id;
        Refresh();
    }

    private void ClearSelection()
    {
        selectedId = null;
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
        if (Selected is not { } item)
            return;
        session.ChangePage(session.Page with { Items = session.Page.Items.Remove(item) });
        ClearSelection();
        Refresh();
    }

    private void AddItem(BoardItem item)
    {
        if (session.Page.Items.Length >= 3000)
        {
            Report("This page already has 3,000 objects. Add a page to continue.");
            return;
        }
        session.ChangePage(session.Page with { Items = session.Page.Items.Add(item) });
        Refresh();
    }

    private void InsertEquation(object sender, RoutedEventArgs args) => Prepare(InsertKind.Equation, "x^2 + 5x + 6 = 0");
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
        toolBeforePlacement = tool;
        placement = (kind, dialog.Source);
        SetTool(Tool.Place);
    }

    private void EditSelected(object sender, RoutedEventArgs args)
    {
        var item = Selected;
        var kind = item switch
        {
            Equation => InsertKind.Equation, Graph => InsertKind.Graph, _ => InsertKind.Text
        };
        var source = item switch
        {
            Equation equation => equation.Source, Graph graph => graph.Source,
            BoardText text => text.Text, _ => null
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
    }

    private void ChooseTool(object sender, RoutedEventArgs args)
    {
        var choice = Enum.Parse<Tool>((string)((Button)sender).Tag);
        CancelGesture();
        if (Selected is not null)
            ClearSelection();
        SetTool(choice);
        Refresh();
    }

    private void SetTool(Tool choice)
    {
        tool = choice;
        DrawingInput.EditingMode = choice is Tool.Pen or Tool.Highlighter ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        DrawingInput.EditingModeInverted = DrawingInput.EditingMode;
        DrawingInput.UseCustomCursor = true;
        DrawingInput.Cursor = choice == Tool.Select ? Cursors.Arrow : Cursors.Cross;
        foreach (var button in new[] { PenButton, MarkerButton, EraseButton, SelectButton, LineButton, CircleButton, ArcButton, RectButton })
            button.Background = (Brush)FindResource((string)button.Tag == choice.ToString() ? "AccentDark" : "Panel");
        ToolHint.Text = choice switch
        {
            Tool.Pen => "Pen · mouse, touch or stylus",
            Tool.Highlighter => "Highlighter · draw to emphasize",
            Tool.Eraser => "Eraser · sweep across objects",
            Tool.Select => "Select · click or drag a board object",
            Tool.Line => "Ruler line · drag endpoints, Shift snaps to 15°",
            Tool.Circle => "Compass circle · drag from the center",
            Tool.Arc => "Compass arc · drag center to start, then click its end",
            Tool.Rectangle => "Rectangle · drag opposite corners",
            _ => "Click the board to place your object. Esc cancels."
        };
        UpdateInk(null, null);
    }

    private void UpdateInk(object? sender, RoutedEventArgs? args)
    {
        if (!ready)
            return;
        DrawingInput.DefaultDrawingAttributes = BoardSurface.InkAttributes(InkColor, ToolWidth,
            SmoothInk.IsChecked == true, tool == Tool.Highlighter);
    }

    private void CollectInk(object sender, InkCanvasStrokeCollectedEventArgs args)
    {
        if (DrawingInput.FinishStroke(args.Stroke) is { } ink)
            AddItem(ink);
    }

    private Ink NewInk(DrawingAttributes attributes)
    {
        var color = attributes.Color;
        var argb = (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
        return new Ink(Guid.NewGuid(), newVisibility, newAnswer, argb,
            attributes.Width, attributes.FitToCurve, attributes.IsHighlighter, []);
    }

    private static BoardPoint PointOf(Point point) => new(
        Math.Clamp(point.X, 0, BoardSurface.BoardWidth), Math.Clamp(point.Y, 0, BoardSurface.BoardHeight));

    private void BoardDown(object sender, MouseButtonEventArgs args)
    {
        if (tool is Tool.Pen or Tool.Highlighter)
        {
            if (Selected is not null)
            {
                ClearSelection();
                Refresh();
            }
            return;
        }
        args.Handled = true;
        var position = args.GetPosition(BoardInput);
        if (tool == Tool.Place && placement is { } value)
        {
            placement = null;
            SetTool(toolBeforePlacement);
            AddItem(ObjectDialog.Create(value.Kind, value.Source, PointOf(position), InkColor, newVisibility, newAnswer));
            return;
        }
        if (tool == Tool.Select)
        {
            selectedId = BoardSurface.Hit(session.Page, position)?.Id;
            if (selectedId is null)
                ClearSelection();
            Refresh();
            if (selectedId is not null && BoardInput.CaptureMouse())
                anchor = position;
            return;
        }
        if (tool == Tool.Arc && pendingArc is { } arc)
        {
            pendingArc = null;
            AddItem(arc with { Sweep = Sweep(arc, position, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) });
            ToolHint.Text = "Compass arc · drag center to start, then click its end";
            return;
        }

        anchor = position;
        BoardInput.CaptureMouse();
        if (tool == Tool.Eraser)
        {
            erasing = [];
            previousErase = null;
            EraseAt(position);
        }
    }

    private void BoardMove(object sender, MouseEventArgs args)
    {
        var position = args.GetPosition(BoardInput);
        if (pendingArc is { } arc)
        {
            TeacherBoard.Mark(null, arc with { Sweep = Sweep(arc, position, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) });
            return;
        }
        if (anchor is not Point start || !BoardInput.IsMouseCaptured)
            return;
        if (tool == Tool.Select)
        {
            if (Selected is not { } item || (!movingSelection && (position - start).Length < 3))
                return;
            if (!movingSelection)
            {
                movingSelection = true;
                TeacherBoard.Edit(session.Page with { Items = session.Page.Items.Remove(item) });
                renderedPage = null;
            }
            TeacherBoard.MarkMoving(item, position - start);
        }
        else if (tool == Tool.Eraser)
            EraseAt(position);
        else
            TeacherBoard.Mark(null, MakeShape(start, position));
    }

    private void BoardUp(object sender, MouseButtonEventArgs args)
    {
        if (anchor is not Point start)
            return;
        args.Handled = true;
        var position = args.GetPosition(BoardInput);
        anchor = null;
        BoardInput.ReleaseMouseCapture();
        if (tool == Tool.Select)
        {
            var moved = movingSelection;
            movingSelection = false;
            if (moved && Selected is { } item)
                Replace(BoardSurface.Move(item, position - start));
            else
                Refresh();
            return;
        }
        if (erasing is { } deleted)
        {
            erasing = null;
            previousErase = null;
            renderedPage = null;
            if (deleted.Count > 0)
                session.ChangePage(session.Page with
                {
                    Items = session.Page.Items.Where(item => !deleted.Contains(item.Id)).ToImmutableArray()
                });
            Refresh();
            return;
        }

        if ((start - position).Length < 2)
        {
            Refresh();
            return;
        }
        var shape = MakeShape(start, position);
        if (tool == Tool.Arc)
        {
            pendingArc = shape;
            TeacherBoard.Mark(null, shape with { ShapeKind = ShapeKind.Circle });
            ToolHint.Text = "Click the arc endpoint. Shift selects the counterclockwise sweep. Esc cancels.";
            return;
        }
        AddItem(shape);
    }

    private Shape MakeShape(Point start, Point end)
    {
        if (tool == Tool.Line && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var delta = end - start;
            var angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 12)) * (Math.PI / 12);
            end = start + new Vector(Math.Cos(angle) * delta.Length, Math.Sin(angle) * delta.Length);
        }
        var kind = tool switch
        {
            Tool.Circle => ShapeKind.Circle, Tool.Rectangle => ShapeKind.Rectangle, Tool.Arc => ShapeKind.Arc,
            _ => ShapeKind.Line
        };
        return new Shape(Guid.NewGuid(), newVisibility, newAnswer, InkColor, InkWidth,
            kind, PointOf(start), PointOf(end));
    }

    private static double Sweep(Shape shape, Point end, bool counterclockwise)
    {
        var startAngle = Math.Atan2(shape.End.Y - shape.Start.Y, shape.End.X - shape.Start.X);
        var endAngle = Math.Atan2(end.Y - shape.Start.Y, end.X - shape.Start.X);
        var clockwise = ((endAngle - startAngle) * 180 / Math.PI + 360) % 360;
        return counterclockwise ? clockwise - 360 : clockwise;
    }

    private void EraseAt(Point position)
    {
        if (erasing is null)
            return;
        var previous = previousErase ?? position;
        previousErase = position;
        var steps = Math.Clamp((int)Math.Ceiling((position - previous).Length / 8), 1, 48);
        var remaining = session.Page with { Items = session.Page.Items.Where(item => !erasing.Contains(item.Id)).ToImmutableArray() };
        var count = erasing.Count;
        for (var i = 1; i <= steps; i++)
        {
            var test = previous + (position - previous) * (i / (double)steps);
            if (BoardSurface.Hit(remaining, test) is not { } item)
                continue;
            erasing.Add(item.Id);
            remaining = remaining with { Items = remaining.Items.Remove(item) };
        }
        if (count != erasing.Count)
            TeacherBoard.Edit(remaining);
    }

    private void BoardCaptureLost(object sender, MouseEventArgs args)
    {
        if (anchor is not null)
        {
            CancelGesture();
            Refresh();
        }
    }

    private void CancelGesture()
    {
        DrawingInput.CancelStroke();
        anchor = null;
        movingSelection = false;
        pendingArc = null;
        if (erasing is not null)
            renderedPage = null;
        erasing = null;
        previousErase = null;
        if (BoardInput.IsMouseCaptured)
            BoardInput.ReleaseMouseCapture();
        if (placement is not null)
        {
            placement = null;
            SetTool(toolBeforePlacement);
        }
        if (tool == Tool.Arc)
            ToolHint.Text = "Compass arc · drag center to start, then click its end";
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
            return;
        Zoom(args.Delta > 0 ? zoom * 1.25 : zoom / 1.25);
        args.Handled = true;
    }

    private IntPtr DisplayMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
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
            window.SetFrame(session.Output);
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
        session.Live = LiveMode.IsChecked == true;
        RefreshProgram();
    }

    private void Freeze(object sender, RoutedEventArgs args)
    {
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
        RefreshProgram();
    }

    private void ExportProgram(object sender, RoutedEventArgs args)
    {
        var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "EduCast program.png" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            AtomicFile.Write(dialog.FileName, stream => ProgramImage.WritePng(session.Output, stream));
            Report("Exported the Program frame as a 1280 × 720 PNG.");
        }
        catch (Exception error) when (FileError(error)) { ShowFileError(error); }
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
