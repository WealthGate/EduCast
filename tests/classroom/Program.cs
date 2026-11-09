using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EduCast.Classroom;
using EduCast.Studio;
using Page = EduCast.Classroom.Page;
using Visibility = EduCast.Classroom.Visibility;

internal static class Checks
{
    private const uint InkColor = 0xff172c39;

    [STAThread]
    public static int Main(string[] args)
    {
        (string, Action)[] tests =
        [
            ("Program projection filters private, hidden and unrevealed objects", Projection),
            ("Manual take, live mode, hold and blackout preserve intended frames", ProgramStates),
            ("Privacy revocation reaches the frozen and published frames immediately", Revocation),
            ("Live ink obeys visibility, answer and Program state without editing the lesson", LiveInkProjection),
            ("Page navigation, history and notes retain editable state", History),
            ("Editable project round trip and failed save preservation", Files),
            ("Invalid project content is rejected", BadFiles),
            ("Structured notation parses scripts, fractions, roots, matrices and chemistry", Notation),
            ("Malformed notation fails safely", InvalidNotation),
            ("Quadratic graph evaluates roots and vertex correctly", Quadratic),
            ("Release catalog offers only newer published Windows installers", ReleaseUpdates),
            ("WPF teacher window, progressive ink and separate clean output", () => Desktop(args)),
            ("WPF selection overlay and committed move preserve object identity and pressure", SelectionMove),
            ("WPF Program PNG encodes visible content and excludes private objects", Snapshot)
        ];
        var failures = 0;
        foreach (var (name, check) in tests)
        {
            if (name.StartsWith("WPF", StringComparison.Ordinal) && args.Contains("--core-only"))
                continue;
            try { check(); Console.WriteLine($"PASS  {name}"); }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}{Environment.NewLine}{error}");
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static BoardText Label(string text, Visibility visibility = Visibility.Both, bool answer = false) =>
        new(Guid.NewGuid(), visibility, answer, InkColor, new(100, 100), 32, text);

    private static void Projection()
    {
        var both = Label("visible");
        var student = Label("student", Visibility.StudentOnly);
        var answer = Label("answer", answer: true);
        var page = new Page("private notes", BoardPaper.Grid, false,
            [both, student, answer, Label("private", Visibility.TeacherOnly), Label("hidden", Visibility.Hidden)]);
        var frame = ProgramFrame.From(page);
        Assert(frame.Paper == BoardPaper.Grid && frame.Items.Select(item => item.Id).SequenceEqual([both.Id, student.Id]));
        Assert(frame.Items.All(item => item.Visibility == Visibility.Both && !item.IsAnswer));
        Assert(ProgramFrame.From(page with { AnswersRevealed = true }).Items.Last().Id == answer.Id);
    }

    private static void ProgramStates()
    {
        var session = new Session();
        var first = Label("published");
        var second = Label("draft");
        session.ChangePage(session.Page with { Items = [first] });
        session.Live = false;
        session.ChangePage(session.Page with { Items = [first, second] });
        Assert(session.Preview.Items.Length == 2 && session.Output!.Items.Length == 1);
        session.ToggleFreeze();
        session.Take();
        Assert(session.Frozen && session.Output!.Items.Single().Id == first.Id);
        session.Blackout = true;
        Assert(session.Output is null);
        session.Blackout = false;
        Assert(session.Output!.Items.Length == 1);
        session.ToggleFreeze();
        Assert(session.Output!.Items.Length == 2);
        session.AddPage();
        Assert(session.Output.Items.Length == 2);
        session.Live = true;
        Assert(session.Output.Items.IsEmpty);
    }

    private static void Revocation()
    {
        var session = new Session();
        var answer = Label("answer", answer: true);
        var approved = Label("currently public");
        var newDraft = Label("new unpublished item");
        session.ChangePage(session.Page with { Items = [answer, approved], AnswersRevealed = true });
        session.Live = false;
        session.ToggleFreeze();
        session.ChangePage(session.Page with
        {
            AnswersRevealed = false,
            Items = [answer, approved with { Visibility = Visibility.TeacherOnly }, newDraft]
        });
        Assert(session.Output!.Items.IsEmpty);
        session.ToggleFreeze();
        Assert(session.Output!.Items.IsEmpty);
        session.Take();
        Assert(session.Output!.Items.Single().Id == newDraft.Id);
    }

    private static void History()
    {
        var session = new Session();
        session.ChangePage(session.Page with { Notes = "Answer key is private", Items = [Label("One")] });
        var frame = session.Output;
        session.ChangePage(session.Page with { Notes = "Revised private notes" });
        Assert(ReferenceEquals(frame, session.Output));
        session.AddPage();
        Assert(session.PageIndex == 1 && session.Lesson.Pages.Length == 2);
        session.Undo();
        Assert(session.PageIndex == 0 && session.Page.Notes == "Revised private notes");
        session.Redo();
        Assert(session.PageIndex == 1);
        session.GoTo(0);
        Assert(session.Page.Items.Single() is BoardText { Text: "One" });
    }

    private static void LiveInkProjection()
    {
        var session = new Session();
        var draft = new Ink(Guid.NewGuid(), Visibility.Both, false, InkColor, 4, true, false, [new(100, 100, .2f)]);
        var initial = session.Output;
        Assert(session.ProjectLiveInk(draft)?.Stroke.Points[0].Pressure == .2f);
        Assert(session.ProjectLiveInk(draft with { Visibility = Visibility.TeacherOnly }) is null);
        Assert(session.ProjectLiveInk(draft with { Visibility = Visibility.Hidden }) is null);
        Assert(session.ProjectLiveInk(draft with { IsAnswer = true }) is null);
        Assert(session.ProjectLiveInk(draft with { Visibility = Visibility.StudentOnly })?.Stroke.Visibility == Visibility.Both);
        session.Live = false;
        session.Take();
        Assert(session.ProjectLiveInk(draft) is null && ReferenceEquals(initial, session.Output));
        session.Live = true;
        session.ToggleFreeze();
        Assert(session.ProjectLiveInk(draft) is null && ReferenceEquals(initial, session.Output));
        session.ToggleFreeze();
        session.Blackout = true;
        Assert(session.ProjectLiveInk(draft) is null);
        session.Blackout = false;
        Assert(session.ProjectLiveInk(draft) is not null && !session.CanUndo && session.Page.Items.IsEmpty);
    }

    private static void Files() => InTemporaryDirectory(directory =>
    {
        var page = new Page("teacher notes", BoardPaper.Dots, false, [
            Label("student text"),
            new Ink(Guid.NewGuid(), Visibility.TeacherOnly, false, InkColor, 4, true, false,
                [new(10, 10, .2f), new(30, 30, .7f)]),
            new Shape(Guid.NewGuid(), Visibility.Both, false, InkColor, 4, ShapeKind.Arc, new(20, 20), new(30, 30), 90),
            new Equation(Guid.NewGuid(), Visibility.Both, true, InkColor, new(35, 35), 42, "sqrt(x^2+y^2)"),
            new Graph(Guid.NewGuid(), Visibility.StudentOnly, false, InkColor, new(40, 40), "y=x^2-4x+3")
        ]);
        var lesson = new Lesson(1, "Round trip", [page, Page.Empty()]);
        var file = Path.Combine(directory, "test.educast");
        LessonFile.Save(file, lesson);
        var reopened = LessonFile.Load(file);
        Assert(reopened.Title == lesson.Title && reopened.Pages.Length == 2 && reopened.Pages[0].Notes == page.Notes);
        Assert(reopened.Pages[0].Items[1] is Ink { Points.Length: 2 } ink && ink.Points[1].Pressure == .7f);
        Assert(reopened.Pages[0].Items[2] is Shape { Sweep: 90 });
        Assert(reopened.Pages[0].Items[3] is Equation { IsAnswer: true });
        Assert(reopened.Pages[0].Items[4] is Graph { Visibility: Visibility.StudentOnly });
        Assert(ProgramFrame.From(reopened.Pages[0]).Items.Length == 3);
        LessonFile.Save(file, reopened with { Title = "Revised" });
        Assert(LessonFile.Load(file).Title == "Revised");
        var before = File.ReadAllBytes(file);
        Expect<InvalidDataException>(() => LessonFile.Save(file, lesson with { Version = 0 }));
        Assert(before.SequenceEqual(File.ReadAllBytes(file)));
        Assert(Directory.GetFiles(directory).Length == 1);
    });

    private static void BadFiles() => InTemporaryDirectory(directory =>
    {
        var file = Path.Combine(directory, "invalid.educast");
        foreach (var invalid in new[]
        {
            "{}", "[]", "{\"version\":1,\"title\":\"x\",\"pages\":[null]}",
            "{\"version\":1,\"title\":\"x\",\"pages\":[{\"notes\":\"\",\"paper\":0,\"items\":[{\"kind\":\"unknown\"}]}]}"
        })
        {
            File.WriteAllText(file, invalid);
            Expect<InvalidDataException>(() => LessonFile.Load(file));
        }
    });

    private static void Notation()
    {
        Assert(Parse("x^2 + 5x + 6 = 0") is Binary
        {
            Operator: "=", Left: Binary { Left: Binary { Left: Scripts { Superscript: Literal { Value: "2" } } } }
        });
        Assert(Parse("((a/b)+c)/(d/e)") is Fraction { Top: Binary, Bottom: Fraction });
        Assert(Parse("sqrt(x^2+y^2)") is Radical { Child: Binary { Left: Scripts, Right: Scripts } });
        Assert(Parse("root(3,x)") is Radical { Index: Literal { Value: "3" } });
        Assert(Parse("H_2SO_4") is Binary { Operator: "", Left: Scripts { Subscript: Literal { Value: "2" } },
            Right: Scripts { Subscript: Literal { Value: "4" } } });
        Assert(Parse("matrix(1,2;3,4)") is EduCast.Classroom.Matrix { Rows.Length: 2 });
        Assert(Parse("(-b +- sqrt(b^2-4ac))/(2a)") is Fraction { Top: Binary { Operator: "±" } });
        Assert(Parse("sin(theta) = opposite/hypotenuse") is Binary { Left: Function, Right: Fraction });
    }

    private static void InvalidNotation()
    {
        foreach (var invalid in new[] { "", "x^", "sqrt(", "x_2_3", "matrix(1,2;3)", "2..0", "x\0junk",
            new string('(', 20) + "x" + new string(')', 20) })
            Assert(!MathParser.TryParse(invalid, out _, out var error) && error.Length > 0, invalid);
        Assert(!MathParser.TryParseGraph("x=y+2", out _, out _));
    }

    private static void Quadratic()
    {
        Assert(MathParser.TryParseGraph("y = x^2 - 4x + 3", out var graph, out var error), error);
        Assert(MathParser.Evaluate(graph!, 1) == 0);
        Assert(MathParser.Evaluate(graph!, 3) == 0);
        Assert(MathParser.Evaluate(graph!, 2) == -1);
        Assert(MathParser.Evaluate(graph!, 0) == 3);
    }

    private static void ReleaseUpdates()
    {
        static JsonElement Release(string tag, string filename, bool draft = false, bool prerelease = false,
            string state = "uploaded") => JsonSerializer.SerializeToElement(new
        {
            tag_name = tag,
            draft,
            prerelease,
            assets = new[] { new { name = filename, state, browser_download_url = "https://example.invalid/installer" } }
        });

        var installed = new Version(0, 3, 0);
        const string tag = "v0.3.1";
        const string installer = "EduCast-Studio-0.3.1-win-x64.msi";
        var update = ReleaseCatalog.ReadUpdate(Release(tag, installer), installed);
        Assert(update?.Version == new Version(0, 3, 1));
        Assert(update!.Installer.AbsoluteUri == $"https://github.com/WealthGate/EduCast/releases/download/{tag}/{installer}");
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer), new Version(0, 3, 1)) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer), new Version(0, 4, 0)) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer, draft: true), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer, prerelease: true), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, "EduCast-Studio-0.3.1-win-x64.zip"), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer, state: "new"), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release("v0.3.1.1", installer), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release("v0.03.1", installer), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(JsonSerializer.SerializeToElement(new { }), installed) is null);
    }

    private static void Desktop(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var teacher = new TeacherWindow(_ => Task.FromResult<AvailableUpdate?>(new AvailableUpdate(
            new Version(0, 3, 1), new Uri("https://github.com/WealthGate/EduCast/releases"))))
        {
            ShowActivated = false
        };
        app.MainWindow = teacher;
        teacher.Show();
        Pump();
        Assert((string)Find<Button>(teacher, "UpdateButton").Content == "Download v0.3.1");
        var scroller = Find<ScrollViewer>(teacher, "BoardScroller");
        Assert(scroller.ScrollableWidth < 1 && scroller.ScrollableHeight < 1, "teacher board must start fitted");

        var program = Find<FrameworkElement>(teacher, "ProgramPreview");
        var clean = Pixel(program, 200, 200);
        var drawing = Find<LiveInkCanvas>(teacher, "DrawingInput");
        static StylusPointCollection Line(int y) => new(new[]
        {
            new StylusPoint(190, y, .3f), new StylusPoint(210, y, .7f)
        });
        void Write(int y)
        {
            var stroke = new Stroke(Line(y), drawing.DefaultDrawingAttributes.Clone());
            drawing.Strokes.Add(stroke);
            drawing.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(stroke) { RoutedEvent = InkCanvas.StrokeCollectedEvent });
        }

        Click(teacher, "StartButton");
        Pump();
        var student = app.Windows.Cast<Window>().Single(window => window.Title.EndsWith("Student Output", StringComparison.Ordinal));
        Assert(student.Content is Grid { Children.Count: 1 } matte && matte.Children[0] is Viewbox
            { Child: FrameworkElement }, "student visual root should contain only a matte and board view");
        var studentBoard = (FrameworkElement)((Viewbox)((Grid)student.Content).Children[0]).Child;
        Assert(Pixel(studentBoard, 200, 200).SequenceEqual(clean));

        drawing.BeginPreview();
        drawing.AddPreview(Line(200));
        Pump();
        Assert(!Pixel(studentBoard, 200, 200).SequenceEqual(clean), "live ink is missing before completion");
        Assert(!Pixel(program, 200, 200).SequenceEqual(clean));
        Assert(!Find<Button>(teacher, "UndoButton").IsEnabled, "preview must not write to lesson history");
        Click(teacher, "FreezeButton");
        Assert(Pixel(studentBoard, 200, 200).SequenceEqual(clean), "freeze must clear transient ink");
        Click(teacher, "FreezeButton");
        Write(200);
        Pump();
        Assert(drawing.Strokes.Count == 0 && !Pixel(studentBoard, 200, 200).SequenceEqual(clean));
        Click(teacher, "FreezeButton");
        Write(260);
        Pump();
        Assert(Pixel(studentBoard, 200, 260).SequenceEqual(clean), "held frame changed");
        Click(teacher, "FreezeButton");
        Pump();
        Assert(!Pixel(studentBoard, 200, 260).SequenceEqual(clean), "live stroke did not reach student view");

        var visibility = Find<ComboBox>(teacher, "VisibilityChoice");
        visibility.SelectedValue = Visibility.TeacherOnly;
        drawing.BeginPreview();
        drawing.AddPreview(Line(320));
        visibility.SelectedValue = Visibility.Both;
        Pump();
        Assert(Pixel(studentBoard, 200, 320).SequenceEqual(clean), "private in-progress ink reached student");
        Write(320);
        Pump();
        Assert(Pixel(studentBoard, 200, 320).SequenceEqual(clean), "private stroke reached student");
        Assert(!Pixel(Find<FrameworkElement>(teacher, "TeacherBoard"), 200, 320).SequenceEqual(clean));

        drawing.BeginPreview();
        drawing.AddPreview(Line(380));
        Pump();
        Assert(!Pixel(studentBoard, 200, 380).SequenceEqual(clean));
        drawing.CancelStroke();
        Assert(Pixel(studentBoard, 200, 380).SequenceEqual(clean), "canceled ink remained visible");

        Click(teacher, "BlackButton");
        Pump();
        Assert(Pixel(studentBoard, 200, 200).SequenceEqual(new byte[] { 0, 0, 0, 255 }));
        Click(teacher, "BlackButton");
        Click(teacher, "StopButton");
        Assert(!app.Windows.Cast<Window>().Contains(student));
        ExtendedDisplay(app, teacher, program);

        // Restore the initial lesson to let the teacher window close without a save prompt.
        for (var i = 0; i < 3; i++)
            Click(teacher, "UndoButton");
        Click(teacher, "SampleButton");
        Pump();
        Assert(Find<TextBox>(teacher, "Notes").Text.Contains("CSEC", StringComparison.Ordinal));
        var capture = Array.IndexOf(args, "--capture");
        if (capture >= 0 && capture + 1 < args.Length)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(teacher.ActualWidth),
                (int)Math.Ceiling(teacher.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(teacher);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(args[capture + 1]);
            encoder.Save(file);
        }
        teacher.Close();
        app.Shutdown();
    }

    private static void ExtendedDisplay(App app, TeacherWindow teacher, FrameworkElement program)
    {
        var displays = WindowsDisplays.Get();
        var teacherDevice = WindowsDisplays.For(teacher);
        var target = displays.FirstOrDefault(display => display.Device != teacherDevice);
        if (target is null)
        {
            Console.WriteLine("SKIP  Win32 fullscreen placement requires a second display");
            return;
        }

        var choice = Find<ComboBox>(teacher, "DisplayChoice");
        choice.SelectedIndex = 1;
        Click(teacher, "StartButton");
        Pump();
        var student = app.Windows.Cast<Window>().OfType<DisplayWindow>().Single();
        var handle = new WindowInteropHelper(student).Handle;
        Assert(GetWindowRect(handle, out var bounds), "student HWND has no screen bounds");
        Assert(WindowsDisplays.For(teacher) == teacherDevice && WindowsDisplays.For(student) == target.Device);
        Assert(bounds.Left == target.Left && bounds.Top == target.Top &&
            bounds.Right - bounds.Left == target.Width && bounds.Bottom - bounds.Top == target.Height,
            $"student fullscreen bounds were {bounds.Left},{bounds.Top} to {bounds.Right},{bounds.Bottom}");
        Console.WriteLine($"PASS  Win32 fullscreen on {target.Label}, " +
            $"DPI scale {VisualTreeHelper.GetDpi(teacher).DpiScaleX:F2}/{VisualTreeHelper.GetDpi(student).DpiScaleX:F2}");

        var aspect = Find<ComboBox>(teacher, "AspectChoice");
        aspect.SelectedIndex = 1;
        Pump();
        student.UpdateLayout();
        var matte = (Grid)student.Content;
        var fitted = (Viewbox)matte.Children[0];
        var viewport = LayoutInformation.GetLayoutSlot(fitted);
        var expectedWidth = Math.Min(matte.ActualWidth, matte.ActualHeight * 4 / 3);
        Assert(Math.Abs(viewport.Width - expectedWidth) < 1 &&
            Math.Abs(viewport.Height - expectedWidth * 3 / 4) < 1, $"incorrect 4:3 viewport {viewport}");
        Assert(Math.Abs(fitted.ActualWidth - expectedWidth) < 1 &&
            Math.Abs(fitted.ActualHeight - expectedWidth * 9 / 16) < 1, "lesson proportions changed in 4:3 mode");
        var origin = fitted.TranslatePoint(new Point(), matte);
        Assert(Math.Abs(origin.X * 2 + fitted.ActualWidth - matte.ActualWidth) < 2 &&
            Math.Abs(origin.Y * 2 + fitted.ActualHeight - matte.ActualHeight) < 2, "lesson is off-center");
        aspect.SelectedIndex = 0;

        var before = Pixel(program, 200, 200);
        teacher.ApplyDisplays(displays.Where(display => display.Device != target.Device).ToArray(), teacherDevice);
        Assert(!app.Windows.Cast<Window>().Contains(student) && choice.SelectedIndex == -1,
            "losing the target must close student output without falling back");
        Assert(Pixel(program, 200, 200).SequenceEqual(before), "losing the display changed the Program frame");
        teacher.ApplyDisplays(displays, teacherDevice);
        Assert(!app.Windows.Cast<Window>().OfType<DisplayWindow>().Any() && choice.SelectedIndex == 1,
            "reconnecting should reselect the display and wait for a deliberate restart");
        Click(teacher, "StartButton");
        Assert(app.Windows.Cast<Window>().OfType<DisplayWindow>().Any());
        Click(teacher, "StopButton");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBounds { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeBounds bounds);

    private static void SelectionMove()
    {
        var ink = new Ink(Guid.NewGuid(), Visibility.Both, false, InkColor, 8, false, false,
            [new(100, 100, .2f), new(140, 100, .8f)]);
        var session = new Session();
        session.ChangePage(session.Page with { Items = [ink] });
        var teacher = new BoardSurface();
        teacher.Edit(session.Page with { Items = [] });
        teacher.MarkMoving(ink, new Vector(200, 100));
        var student = new BoardSurface();
        student.Present(session.Output);
        var size = new Size(BoardSurface.BoardWidth, BoardSurface.BoardHeight);
        foreach (var board in new[] { teacher, student })
        {
            board.Measure(size);
            board.Arrange(new Rect(size));
        }
        var paper = Pixel(teacher, 220, 220);
        Assert(!Pixel(teacher, 320, 200).SequenceEqual(paper));
        Assert(Pixel(student, 320, 200).SequenceEqual(paper), "drag preview reached Program");
        Assert(!Pixel(student, 120, 100).SequenceEqual(paper));

        var moved = (Ink)BoardSurface.Move(ink, new Vector(200, 100));
        Assert(moved.Id == ink.Id && moved.Points[0] == new BoardPoint(300, 200, .2f));
        Assert(moved.Points[1] == new BoardPoint(340, 200, .8f));
        Assert(ReferenceEquals(BoardSurface.Move(ink, new Vector()), ink));
        session.ChangePage(session.Page with { Items = [moved] });
        student.Present(session.Output);
        Assert(!Pixel(student, 320, 200).SequenceEqual(paper));
        Assert(Pixel(student, 120, 100).SequenceEqual(paper));
        session.Undo();
        Assert(session.Page.Items.Single() == ink);
    }

    private static void Snapshot()
    {
        using var png = new MemoryStream();
        var visible = new Shape(Guid.NewGuid(), Visibility.Both, false, InkColor, 8,
            ShapeKind.Line, new(110, 100), new(200, 100));
        var secret = visible with
        {
            Id = Guid.NewGuid(), Visibility = Visibility.TeacherOnly, Start = new(110, 200), End = new(200, 200)
        };
        ProgramImage.WritePng(ProgramFrame.From(new Page("private notes", BoardPaper.Plain, false, [visible, secret])), png);
        png.Position = 0;
        var image = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert(image.PixelWidth == 1280 && image.PixelHeight == 720);
        Assert(!Pixel(image, 150, 100).SequenceEqual(Pixel(image, 150, 150)));
        Assert(Pixel(image, 150, 200).SequenceEqual(Pixel(image, 150, 150)));
    }

    private static void Click(Window window, string name) =>
        Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Find<T>(Window window, string name) => (T)window.FindName(name);

    private static byte[] Pixel(FrameworkElement element, int x, int y)
    {
        var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return Pixel(bitmap, x, y);
    }

    private static byte[] Pixel(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel;
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static MathNode Parse(string value)
    {
        Assert(MathParser.TryParse(value, out var node, out var error), error);
        return node!;
    }

    private static void Assert(bool condition, string message = "Assertion failed")
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void InTemporaryDirectory(Action<string> action)
    {
        var directory = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,
            "checks-" + Guid.NewGuid().ToString("N")));
        try { action(directory.FullName); }
        finally
        {
            foreach (var file in directory.GetFiles())
                file.Delete();
            directory.Delete();
        }
    }
}
