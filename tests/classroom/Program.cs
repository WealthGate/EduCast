using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

internal static partial class Checks
{
    private const uint InkColor = 0xff172c39;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--capture-fixture", var title])
            return CaptureFixture(title);
        if (args is ["--streaming", var engine])
        {
            try
            {
                Streaming(engine);
                Console.WriteLine("PASS  Media files, mixed audio and independent RTMP destinations");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        (string, Action)[] tests =
        [
            ("Program projection filters private, hidden and unrevealed objects", Projection),
            ("Manual take, live mode, hold and blackout preserve intended frames", ProgramStates),
            ("Privacy revocation reaches the frozen and published frames immediately", Revocation),
            ("Live ink obeys visibility, answer and Program state without editing the lesson", LiveInkProjection),
            ("Transient object movement respects Program privacy and state", ObjectGestureProjection),
            ("Page navigation, history and notes retain editable state", History),
            ("Editable project round trip and failed save preservation", Files),
            ("Invalid project content is rejected", BadFiles),
            ("Structured notation parses scripts, fractions, roots, matrices and chemistry", Notation),
            ("Malformed notation fails safely", InvalidNotation),
            ("Chemistry formats and checks the atom counts and charges of reactions", ChemicalNotation),
            ("Subject pages, backgrounds, privacy and editable diagram files", SubjectPages),
            ("WPF smart ink recognizes supported shapes and keeps object visibility", SmartSketches),
            ("WPF handwritten notation recognizes fractions, roots, scripts and division", SmartNotation),
            ("WPF smart ink recognizes multi-sided polygons and solid sketches", SolidSketches),
            ("WPF instrument controls leave the measuring references uncovered", InstrumentClearance),
            ("WPF subject diagrams render and move as board objects", SubjectDrawing),
            ("Quadratic graph evaluates roots and vertex correctly", Quadratic),
            ("Release catalog offers only newer published Windows installers", ReleaseUpdates),
            ("Update download verifies bytes and checksum and rejects an unsafe redirect", DownloadUpdate),
            ("Classroom clock follows local time and stopwatch/countdown use monotonic time", ClockControls),
            ("Math instrument scales, edge projection, angles and exact guide geometry", MathGeometry),
            ("Physics instrument graduations, screw pitch and specimen contact", GaugeArithmetic),
            ("Scientific calculator evaluates expressions and rejects undefined results", Calculator),
            ("DirectShow enumeration separates cameras and microphones", DeviceListing),
            ("Streaming destination validation and bounded audio mixing", BroadcastInputs),
            ("WPF toolbar rows distribute controls and retain close targets", ToolbarRows),
            ("WPF teacher window, progressive ink and separate clean output", () => Desktop(args)),
            ("WPF selection overlay and committed move preserve object identity and pressure", SelectionMove),
            ("WPF edge and corner drags resize board objects while preserving their content", SelectionResize),
            ("WPF rapid dots keep writing and only a completed double-click selects", InkTapGestures),
            ("WPF double-click selection takes precedence over a pending placement or eraser tap", ObjectDoubleClicks),
            ("WPF eraser sweeps split scaled ink and preserve private output", CanvasSweeps),
            ("WPF a marquee selects and moves visible objects as a group", MarqueeSelection),
            ("WPF Program PNG encodes visible content and excludes private objects", Snapshot),
            ("Teaching browser accepts web addresses and supported lesson files", TeachingAddresses),
            ("WPF source composition renders arrows, pen marks and a transient pointer", SourceComposition),
            ("WPF clean Program camera and clock composition rasterizes at UHD", StudioComposition),
            ("WPF optional FFmpeg capture and 720p/1080p/UHD recording smoke test", MediaSmoke)
        ];
        var failures = 0;
        foreach (var (name, check) in tests)
        {
            if (name.StartsWith("WPF", StringComparison.Ordinal) && args.Contains("--core-only"))
                continue;
            if (name.Contains("optional FFmpeg", StringComparison.Ordinal) && !args.Contains("--media"))
                continue;
            try { check(); Console.WriteLine($"PASS  {name}"); }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}{Environment.NewLine}{error}");
            }
        }
        var msi = Array.IndexOf(args, "--verify-msi");
        if (msi >= 0 && msi + 1 < args.Length)
        {
            try
            {
                MsiUpdatePackage.Verify(args[msi + 1], ReleaseCatalog.InstalledVersion);
                Expect<InvalidDataException>(() => MsiUpdatePackage.Verify(args[msi + 1], new Version(0, 0, 0)));
                Console.WriteLine("PASS  Actual MSI identity, upgrade family, per-user scope and release version");
            }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  Actual MSI verification{Environment.NewLine}{error}");
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
            new Graph(Guid.NewGuid(), Visibility.StudentOnly, false, InkColor, new(40, 40), "y=x^2-4x+3"),
            new Ink(Guid.NewGuid(), Visibility.Both, false, InkColor, 4, true, false, [new(20, 20)], InkTip.Square),
            new Shape(Guid.NewGuid(), Visibility.Both, false, InkColor, 4, ShapeKind.Arrow, new(20, 20), new(30, 30))
            {
                Placement = new ObjectPlacement(1.75, -18, -22)
            }
        ]);
        var lesson = new Lesson(1, "Round trip", [page, Page.Empty()]);
        var file = Path.Combine(directory, "test.educast");
        LessonFile.Save(file, lesson);
        var reopened = LessonFile.Load(file);
        Assert(reopened.Title == lesson.Title && reopened.Pages.Length == 2 && reopened.Pages[0].Notes == page.Notes);
        Assert(reopened.Pages[0].Items[1] is Ink { Points.Length: 2, Tip: InkTip.Round } ink && ink.Points[1].Pressure == .7f);
        Assert(reopened.Pages[0].Items[2] is Shape { Sweep: 90 });
        Assert(reopened.Pages[0].Items[3] is Equation { IsAnswer: true });
        Assert(reopened.Pages[0].Items[4] is Graph { Visibility: Visibility.StudentOnly });
        Assert(reopened.Pages[0].Items[5] is Ink { Tip: InkTip.Square });
        Assert(reopened.Pages[0].Items[6] is Shape { ShapeKind: ShapeKind.Arrow, Placement: { Scale: 1.75, X: -18, Y: -22 } });
        Assert(reopened.Pages[0].Items[0].Placement is null);
        Assert(ProgramFrame.From(reopened.Pages[0]).Items.Length == 5);
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
        var digest = "sha256:" + new string('a', 64);
        static JsonElement Release(string tag, string filename, bool draft = false, bool prerelease = false,
            string state = "uploaded", string? digest = null, long size = 42) => JsonSerializer.SerializeToElement(new
        {
            tag_name = tag,
            draft,
            prerelease,
            assets = new[] { new { name = filename, state, digest, size, browser_download_url = "https://example.invalid/installer" } }
        });

        var installed = new Version(0, 3, 0);
        const string tag = "v0.3.1";
        const string installer = "EduCast-Studio-0.3.1-win-x64.msi";
        var update = ReleaseCatalog.ReadUpdate(Release(tag, installer, digest: digest), installed);
        Assert(update?.Version == new Version(0, 3, 1));
        Assert(update!.Installer.AbsoluteUri == $"https://github.com/WealthGate/EduCast/releases/download/{tag}/{installer}");
        Assert(update.Size == 42 && update.Sha256 == digest[7..]);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer, digest: digest), new Version(0, 3, 1)) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer, digest: digest), new Version(0, 4, 0)) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer, draft: true, digest: digest), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer, prerelease: true, digest: digest), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, "EduCast-Studio-0.3.1-win-x64.zip", digest: digest), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer, state: "new", digest: digest), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release("v0.3.1.1", installer, digest: digest), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release("v0.03.1", installer, digest: digest), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(Release(tag, installer, digest: digest, size: 0), installed) is null);
        Assert(ReleaseCatalog.ReadUpdate(JsonSerializer.SerializeToElement(new { }), installed) is null);
    }

    private sealed class UpdateHttp(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request.RequestUri!));
    }

    private static void DownloadUpdate() => InTemporaryDirectory(directory =>
    {
        byte[] bytes = [10, 20, 30, 40, 50];
        var checksum = Convert.ToHexString(SHA256.HashData(bytes));
        var release = new AvailableUpdate(new Version(0, 6, 1),
            new Uri("https://github.com/WealthGate/EduCast/releases/download/v0.6.1/EduCast-Studio-0.6.1-win-x64.msi"),
            bytes.Length, checksum);
        var file = Path.Combine(directory, "update test.msi");
        using var trusted = new HttpClient(new UpdateHttp(uri => uri.Host == "github.com"
            ? new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://release-assets.githubusercontent.com/example") }
            }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        UpdateDownload.SaveAsync(trusted, release, file, default).GetAwaiter().GetResult();
        Assert(File.ReadAllBytes(file).SequenceEqual(bytes));
        InAppUpdate.VerifyStagedFileAsync(file, release.Size, checksum).GetAwaiter().GetResult();
        Expect<InvalidDataException>(() => InAppUpdate.VerifyStagedFileAsync(file, release.Size,
            new string('0', 64)).GetAwaiter().GetResult());
        var command = InAppUpdate.InstallerCommand(file);
        Assert(Path.GetFileName(command.FileName) == "msiexec.exe" &&
            command.ArgumentList.SequenceEqual(["/i", file, "/passive", "/norestart"]));
        File.Delete(file);
        Expect<InvalidDataException>(() => UpdateDownload.SaveAsync(trusted,
            release with { Sha256 = new string('0', 64) }, file, default).GetAwaiter().GetResult());
        Assert(!File.Exists(file), "a rejected payload must be removed");
        using var untrusted = new HttpClient(new UpdateHttp(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://example.invalid/update.msi") }
        }));
        Expect<InvalidDataException>(() => UpdateDownload.SaveAsync(untrusted,
            release, file, default).GetAwaiter().GetResult());
        Assert(!File.Exists(file));
    });

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Wall { get; set; } = new(2026, 11, 9, 10, 20, 30, TimeSpan.Zero);
        public TimeSpan Monotonic { get; set; }
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Monotonic.Ticks;
        public override DateTimeOffset GetUtcNow() => Wall;
    }

    private static void ClockControls()
    {
        var time = new TestTime();
        var clock = new ClassroomClock(time);
        Assert(clock.Read().Text == "10:20:30");
        clock.Select(ClockView.Analog, TimeSpan.FromSeconds(3));
        Assert(clock.Read().View == ClockView.Analog);
        clock.Select(ClockView.Stopwatch, TimeSpan.FromSeconds(3));
        clock.Toggle();
        time.Monotonic += TimeSpan.FromMilliseconds(2400);
        time.Wall += TimeSpan.FromHours(4);
        Assert(clock.Read().Text == "00:00:02");
        clock.Toggle();
        time.Monotonic += TimeSpan.FromSeconds(2);
        Assert(clock.Read().Text == "00:00:02");
        clock.Select(ClockView.Countdown, TimeSpan.FromSeconds(3));
        clock.Toggle();
        time.Monotonic += TimeSpan.FromMilliseconds(1100);
        Assert(clock.Read().Text == "00:00:02");
        time.Monotonic += TimeSpan.FromSeconds(4);
        Assert(clock.Read().Value == TimeSpan.Zero && !clock.Running);
        clock.Toggle();
        Assert(!clock.Running);
        clock.Reset();
        Assert(clock.Read().Text == "00:00:03");
    }

    private static void DeviceListing()
    {
        const string listing = """
            [dshow @ 0x1] "USB camera" (video)
            [dshow @ 0x1]   Alternative name "@device_sw_usb"
            [dshow @ 0x1] "Integrated Camera" (none)
            [dshow @ 0x1]   Alternative name "@device_pnp_abc{65E8773D-123}"
            [dshow @ 0x1] "Microphone" (audio)
            [dshow @ 0x1]   Alternative name "@device_sw_mic"
            """;
        foreach (var input in new[] { listing, listing.Replace("dshow @", "in#0 @", StringComparison.Ordinal) })
        {
            var devices = FfmpegMedia.ParseDevices(input);
            Assert(devices.Cameras.Count == 2 && devices.Cameras[0].Id == "@device_sw_usb" &&
                devices.Cameras[1].Name == "Integrated Camera");
            Assert(devices.Microphones.SequenceEqual([new MicrophoneDevice("Microphone", "@device_sw_mic")]));
        }
    }

    private static void MathGeometry()
    {
        var ruler = InstrumentPose.Create(InstrumentKind.Ruler) with { Rotation = 90, Inches = true };
        Assert(Math.Abs(ruler.UnitsPerMeasure - 81.28) < 1e-9);
        var rotated = ruler.ToBoard(new BoardPoint(110, 0));
        Assert(Math.Abs(rotated.X - ruler.Origin.X) < 1e-9 && Math.Abs(rotated.Y - ruler.Origin.Y - 110) < 1e-9);
        var edge = ruler.Edges()[0];
        Assert(edge.Closest(new BoardPoint(48, 13)) == new BoardPoint(48, 0));
        Assert(edge.Closest(new BoardPoint(-30, 13)) == new BoardPoint(0, 0));

        var protractor = InstrumentPose.Create(InstrumentKind.Protractor180) with { Rotation = 27 };
        Assert(Math.Abs(protractor.ReadAngle(protractor.ToBoard(InstrumentPose.Polar(-127, 100))) - 127) < 1e-8);
        Assert(protractor.ReadAngle(protractor.ToBoard(InstrumentPose.Polar(35, 100))) == 0);
        var full = protractor with { Kind = InstrumentKind.Protractor360 };
        Assert(Math.Abs(full.ReadAngle(full.ToBoard(InstrumentPose.Polar(35, 100))) - 325) < 1e-8);
        Assert(InstrumentPose.SweepStep(179, -179) == 2 && InstrumentPose.SweepStep(-179, 179) == -2);

        var square = InstrumentPose.Create(InstrumentKind.SetSquare30);
        Assert(square.Edges().Length == 3);
        Assert(Math.Abs(Math.Atan(square.TriangleHeight / square.Size) * 180 / Math.PI - 30) < 1e-8);
        Assert(InstrumentPose.Create(InstrumentKind.SetSquare45).TriangleHeight == square.Size);
        var curve = InstrumentPose.Create(InstrumentKind.FrenchCurve);
        var points = curve.TraceCurve(-curve.Size / 2, curve.Size / 2);
        Assert(points[points.Length / 2] == curve.Origin);
        Assert(Math.Abs(points[24].Y - curve.Origin.Y + curve.CurveRise / 4) < 1e-8);
        Assert(points[0].Y == points[^1].Y && points[0].Y < curve.Origin.Y);
    }

    private static void Calculator()
    {
        Assert(MathParser.TryCalculate("(3/4)^2", true, out var fraction, out _) && fraction == .5625);
        Assert(MathParser.TryCalculate("sqrt(81)+sin(30)", true, out var degrees, out _) && Math.Abs(degrees - 9.5) < 1e-10);
        Assert(MathParser.TryCalculate("asin(0.5)", true, out var inverse, out _) && Math.Abs(inverse - 30) < 1e-10);
        Assert(MathParser.TryCalculate("sin(pi/2)", false, out var radians, out _) && Math.Abs(radians - 1) < 1e-10);
        Assert(!MathParser.TryCalculate("x+2", false, out _, out _));
        Assert(!MathParser.TryCalculate("1/0", false, out _, out _));
        Assert(!MathParser.TryCalculate("tan(90)", true, out _, out _));
        Assert(!MathParser.TryCalculate("root(0,1)", true, out _, out _));
    }

    private static void Desktop(string[] args) =>
        InTemporaryDirectory(directory => Desktop(args, Path.Combine(directory, "theme.txt")));

    private static void Desktop(string[] args, string appearancePath)
    {
        var app = new App(appearancePath);
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var installed = ReleaseCatalog.InstalledVersion;
        var newer = new Version(installed.Major, installed.Minor, installed.Build + 1);
        var teacher = new TeacherWindow(_ => Task.FromResult<AvailableUpdate?>(new AvailableUpdate(
            newer, new Uri("https://github.com/WealthGate/EduCast/releases"), 42, "")))
        {
            ShowActivated = false
        };
        app.MainWindow = teacher;
        teacher.Show();
        Pump();
        Assert((string)Find<Button>(teacher, "UpdateButton").Content == $"Update to v{newer}");
        Click(teacher, "UpdateButton");
        Pump();
        Assert(teacher.IsVisible && Find<Button>(teacher, "UpdateButton").IsEnabled,
            "a rejected download must leave EduCast open and the update action available");
        var scroller = Find<ScrollViewer>(teacher, "BoardScroller");
        Assert(scroller.ScrollableWidth < 1 && scroller.ScrollableHeight < 1, "teacher board must start fitted");

        var program = Find<FrameworkElement>(teacher, "ProgramPreview");
        var clean = Pixel(program, 200, 200);
        AppearanceChecks(app, teacher, program, appearancePath);
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
        teacher.ShowBroadcastOutput();
        var online = app.Windows.Cast<Window>().Single(window => window.Title == "EduCast · Online Program");
        var onlineBoard = (FrameworkElement)((Viewbox)((Grid)online.Content).Children[0]).Child;
        Assert(online.Owner is null && Pixel(onlineBoard, 200, 200).SequenceEqual(clean));

        drawing.BeginPreview();
        drawing.AddPreview(Line(200));
        Pump();
        Assert(!Pixel(studentBoard, 200, 200).SequenceEqual(clean), "live ink is missing before completion");
        Assert(!Pixel(program, 200, 200).SequenceEqual(clean));
        Assert(!Pixel(onlineBoard, 200, 200).SequenceEqual(clean), "live ink is missing from Online Program");
        Assert(!Find<Button>(teacher, "UndoButton").IsEnabled, "preview must not write to lesson history");
        Click(teacher, "FreezeButton");
        Assert(Pixel(studentBoard, 200, 200).SequenceEqual(clean), "freeze must clear transient ink");
        Click(teacher, "FreezeButton");
        Write(200);
        Pump();
        Assert(drawing.Strokes.Count == 0 && !Pixel(studentBoard, 200, 200).SequenceEqual(clean));
        var objects = Find<ListBox>(teacher, "Objects");
        foreach (var tool in new[] { "PointerButton", "EraseButton", "LineButton", "ArcButton" })
        {
            Click(teacher, tool);
            var point = new Point(200, 200);
            Assert(!teacher.TrySelectAt(point, 1) && objects.SelectedItem is null);
            Assert(teacher.TrySelectAt(point, 2) && objects.SelectedItem is not null && objects.Items.Count == 1,
                $"double-click with {tool} must select the existing stroke");
        }
        Click(teacher, "PenButton");
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
        Assert(Pixel(onlineBoard, 200, 320).SequenceEqual(clean), "private stroke reached Online Program");
        Assert(!Pixel(Find<FrameworkElement>(teacher, "TeacherBoard"), 200, 320).SequenceEqual(clean));

        drawing.BeginPreview();
        drawing.AddPreview(Line(380));
        Pump();
        Assert(!Pixel(studentBoard, 200, 380).SequenceEqual(clean));
        drawing.CancelStroke();
        Assert(Pixel(studentBoard, 200, 380).SequenceEqual(clean), "canceled ink remained visible");

        var red = SolidFrame(Colors.Red);
        var blue = SolidFrame(Colors.Blue);
        teacher.ApplyStudioLayers(new ProgramLayers(red, Clock: new(ClockView.Digital, TimeSpan.FromHours(10))));
        Pump();
        Assert(Pixel(studentBoard, 980, 555)[2] > 220);
        Assert(teacher.BeginClockDrag(new Point(1100, 80), source: false));
        teacher.MoveClock(new Point(800, 240));
        Pump();
        Assert(!Pixel(studentBoard, 800, 240).SequenceEqual(clean) &&
            Pixel(studentBoard, 1100, 80).SequenceEqual(clean), "clock did not move live");
        teacher.CancelClockDrag(source: false);
        var clockClose = CanvasClose.Center(ProgramSurface.ClockBounds(new(ClockView.Digital, TimeSpan.Zero)),
            BoardSelection.Unit(Find<FrameworkElement>(teacher, "BoardSlot").Width));
        Assert(Find<TeacherInstrumentSurface>(teacher, "TeacherInstrument").CloseAt(clockClose));
        Pump();
        Assert(Pixel(studentBoard, 1100, 80).SequenceEqual(clean), "closed clock remained on Program");
        teacher.ApplyStudioLayers(new ProgramLayers(red, Clock: new(ClockView.Digital, TimeSpan.FromHours(10))));
        Click(teacher, "FreezeButton");
        teacher.ApplyStudioLayers(new ProgramLayers(blue));
        Pump();
        Assert(Pixel(studentBoard, 980, 555)[2] > 220, "freeze changed the camera frame");
        Assert(Pixel(studentBoard, 1100, 80).SequenceEqual(clean), "unpublished clock remained in the hold");
        Click(teacher, "BlackButton");
        Pump();
        Assert(Pixel(studentBoard, 200, 200).SequenceEqual(new byte[] { 0, 0, 0, 255 }));
        Assert(Pixel(studentBoard, 980, 555).SequenceEqual(new byte[] { 0, 0, 0, 255 }));
        Assert(Pixel(onlineBoard, 980, 555).SequenceEqual(new byte[] { 0, 0, 0, 255 }));
        Click(teacher, "BlackButton");
        teacher.ApplyStudioLayers(new ProgramLayers());
        Pump();
        Assert(Pixel(studentBoard, 980, 555).SequenceEqual(clean), "unpublished camera remained in the hold");
        Click(teacher, "FreezeButton");
        InstrumentDemonstration(teacher, studentBoard, clean);
        GaugeDemonstration(teacher, studentBoard, clean);
        SourceInstruments(app, teacher, args);
        Click(teacher, "StopButton");
        Assert(!app.Windows.Cast<Window>().Contains(student));
        Assert(online.IsVisible);
        online.Close();
        ExtendedDisplay(app, teacher, program);
        Click(teacher, "StudioButton");
        Pump();
        var studio = app.Windows.Cast<Window>().OfType<StudioTools>().Single();
        Assert(studio.IsVisible && ReferenceEquals(studio.Owner, teacher));
        studio.Close();
        Assert(!studio.IsVisible);

        // Restore the initial lesson to let the teacher window close without a save prompt.
        for (var i = 0; i < 3; i++)
            Click(teacher, "UndoButton");
        Click(teacher, "SampleButton");
        Pump();
        Assert(Find<TextBox>(teacher, "Notes").Text.Contains("CSEC", StringComparison.Ordinal));
        var sources = Array.IndexOf(args, "--teaching-sources");
        if (sources >= 0 && sources + 1 < args.Length)
            TeachingSources(app, teacher, program, args[sources + 1]);
        var capture = Array.IndexOf(args, "--capture");
        if (capture >= 0 && capture + 1 < args.Length)
        {
            if (args.Contains("--capture-instruments"))
            {
                Click(teacher, "MathToolsButton");
                Pump();
            }
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

    private static void SourceInstruments(App app, TeacherWindow teacher, string[] args)
    {
        var choices = Find<ComboBox>(teacher, "InstrumentChoice");
        var placed = Find<ComboBox>(teacher, "PlacedInstruments");
        choices.SelectedIndex = 0;
        Click(teacher, "AddInstrumentButton");
        Click(teacher, "ResumeSourceButton");
        var source = app.Windows.OfType<SourceWindow>().Single();
        teacher.OpenInstruments(source: true);
        choices.SelectedIndex = 1;
        Click(teacher, "AddInstrumentButton");
        Pump();
        Assert(placed.Items.Count == 2 && Find<StackPanel>(source, "InstrumentHost").Children.Count == 1);
        Assert(teacher.BeginMathDrag(new BoardPoint(700, 412), source: true));
        teacher.EndMathDrag(new BoardPoint(750, 412), snap: false);
        var sourceOverlay = Find<TeacherInstrumentSurface>(source, "SourceInstruments");
        var movedRuler = new InstrumentDisplay(InstrumentPose.Create(InstrumentKind.Ruler, 40) with { Origin = new BoardPoint(505, 376) });
        Assert(sourceOverlay.CloseAt(CanvasClose.Center(InstrumentDrawing.Bounds(movedRuler),
            BoardSelection.Unit(Find<FrameworkElement>(source, "SourceStage").ActualWidth))) &&
            placed.Items.Count == 1, "source instrument close button did not remove the active instrument");
        CaptureLayouts(app, teacher, source, args);
        var sourceTheme = Find<ComboBox>(source, "ThemePicker");
        sourceTheme.SelectedItem = WorkspaceTheme.Daylight;
        Pump();
        Assert(app.Appearance.Selected == WorkspaceTheme.Daylight &&
            Equals(Find<ComboBox>(teacher, "ThemePicker").SelectedItem, WorkspaceTheme.Daylight));
        app.Appearance.Selected = WorkspaceTheme.Violet;
        var sourceToggle = Find<Button>(source, "SourceMathToolsButton");
        sourceToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert(Find<FrameworkElement>(teacher, "InstrumentBar").Visibility == System.Windows.Visibility.Collapsed);
        sourceToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert(Find<FrameworkElement>(teacher, "InstrumentBar").Visibility == System.Windows.Visibility.Visible);
        teacher.ReturnToBoard();
        Pump();
        Assert(placed.Items.Count == 1 && Find<StackPanel>(teacher, "BoardInstrumentHost").Children.Count == 1);
        source.Close();
        Click(teacher, "RemoveInstrumentButton");
    }

    private static void InstrumentDemonstration(TeacherWindow teacher, FrameworkElement student, byte[] paper)
    {
        static void Check(TeacherWindow window, string name, bool state)
        {
            var check = Find<CheckBox>(window, name);
            check.IsChecked = state;
            check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        Click(teacher, "MathToolsButton");
        Pump();
        var count = Find<ComboBox>(teacher, "PlacedInstruments").Items.Count;
        Click(teacher, "MathToolsButton");
        Assert(Find<FrameworkElement>(teacher, "InstrumentBar").Visibility == System.Windows.Visibility.Collapsed &&
            Find<ComboBox>(teacher, "PlacedInstruments").Items.Count == count, "collapsing controls removed the instrument");
        Click(teacher, "MathToolsButton");
        var overlay = Find<FrameworkElement>(teacher, "TeacherInstrument");
        Assert(Pixel(overlay, 650, 410)[3] > 0, "teacher ruler is missing");
        Assert(Pixel(student, 650, 410).SequenceEqual(paper), "private ruler leaked");
        Check(teacher, "PublishInstrument", true);
        Pump();
        Assert(!Pixel(student, 650, 410).SequenceEqual(paper), "published ruler is missing");
        Assert(Pixel(student, 423, 412).SequenceEqual(paper), "teacher rotation handle leaked");

        Click(teacher, "FreezeButton");
        Assert(teacher.BeginMathDrag(new BoardPoint(650, 410)));
        teacher.EndMathDrag(new BoardPoint(650, 300), snap: false);
        Pump();
        Assert(!Pixel(student, 650, 410).SequenceEqual(paper) && Pixel(student, 650, 300).SequenceEqual(paper));
        Check(teacher, "PublishInstrument", false);
        Pump();
        Assert(Pixel(student, 650, 410).SequenceEqual(paper), "revoked ruler remained frozen");
        Click(teacher, "FreezeButton");

        Find<TextBox>(teacher, "ReferenceLength").Text = "5";
        Click(teacher, "CalibrateButton");
        Assert(teacher.BeginMathDrag(new BoardPoint(150, 520)));
        teacher.UpdateMathDrag(new BoardPoint(350, 520), snap: false);
        Pump();
        Assert(Pixel(student, 250, 520).SequenceEqual(paper), "private ruler calibration leaked");
        teacher.EndMathDrag(new BoardPoint(350, 520), snap: false);
        Assert(Find<TextBlock>(teacher, "InstrumentStatus").Text.StartsWith("10.4", StringComparison.Ordinal) ||
            Find<TextBlock>(teacher, "InstrumentStatus").Text.StartsWith("10,4", StringComparison.Ordinal));

        Check(teacher, "TraceInstrument", true);
        Assert(teacher.BeginMathDrag(new BoardPoint(540, 275)));
        teacher.UpdateMathDrag(new BoardPoint(800, 278), snap: false);
        Pump();
        Assert(!Pixel(student, 670, 266).SequenceEqual(paper), "projected ruler trace missing");
        teacher.EndMathDrag(new BoardPoint(800, 278), snap: false);
        Pump();
        Assert(!Pixel(student, 670, 266).SequenceEqual(paper));
        Click(teacher, "UndoButton");

        var choice = Find<ComboBox>(teacher, "InstrumentChoice");
        var placed = Find<ComboBox>(teacher, "PlacedInstruments");
        choice.SelectedIndex = 1;
        Click(teacher, "AddInstrumentButton");
        Assert(placed.Items.Count == 2, "adding a compass replaced the ruler");
        placed.SelectedIndex = 0;
        Click(teacher, "RemoveInstrumentButton");
        Click(teacher, "RemoveInstrumentButton");
        Click(teacher, "AddInstrumentButton");
        Assert(teacher.BeginMathDrag(new BoardPoint(581, 376)));
        teacher.EndMathDrag(new BoardPoint(455, 521), snap: false);
        Click(teacher, "InstrumentDrawButton");
        Pump();
        Assert(!Pixel(student, 455, 521).SequenceEqual(paper), "the compass pencil did not land at the selected point");
        Click(teacher, "UndoButton");
        Click(teacher, "RemoveInstrumentButton");
        Click(teacher, "AddInstrumentButton");
        Check(teacher, "TraceInstrument", true);
        Assert(teacher.BeginMathDrag(new BoardPoint(581, 376)));
        teacher.UpdateMathDrag(new BoardPoint(455, 502), snap: false);
        Pump();
        Assert(!Pixel(student, 544, 465).SequenceEqual(paper), "live compass arc missing");
        teacher.EndMathDrag(new BoardPoint(581, 376), snap: false);
        Pump();
        Assert(!Pixel(student, 544, 465).SequenceEqual(paper), "reversing erased the drawn arc");
        Click(teacher, "UndoButton");

        Click(teacher, "RemoveInstrumentButton");
        choice.SelectedIndex = 2;
        Click(teacher, "AddInstrumentButton");
        Click(teacher, "InstrumentDrawButton");
        Pump();
        Assert(!Pixel(student, 530, 376).SequenceEqual(paper), "protractor baseline missing");
        Click(teacher, "UndoButton");

        Click(teacher, "RemoveInstrumentButton");
        choice.SelectedIndex = 5;
        Click(teacher, "AddInstrumentButton");
        Click(teacher, "InstrumentDrawButton");
        Pump();
        Assert(!Pixel(student, 455, 315).SequenceEqual(paper), "set-square leg missing");
        Click(teacher, "UndoButton");

        Click(teacher, "RemoveInstrumentButton");
        choice.SelectedIndex = 6;
        Click(teacher, "AddInstrumentButton");
        Check(teacher, "TraceInstrument", true);
        Assert(teacher.BeginMathDrag(new BoardPoint(265, 244)));
        teacher.EndMathDrag(new BoardPoint(645, 244), snap: false);
        Pump();
        Assert(!Pixel(student, 455, 376).SequenceEqual(paper), "French-curve vertex missing");
        Click(teacher, "UndoButton");

        Click(teacher, "RemoveInstrumentButton");
        choice.SelectedIndex = 7;
        Click(teacher, "AddInstrumentButton");
        Check(teacher, "ShowCalculatorKeys", true);
        var keys = Find<Panel>(teacher, "CalculatorKeys").Children.OfType<Button>().ToArray();
        foreach (var value in new[] { "7", "+", "2" })
            keys.Single(button => (string)button.Content == value).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(Find<TextBox>(teacher, "CalculatorEntry").Text == "7+2");
        Find<TextBox>(teacher, "CalculatorEntry").Text = "sin(30)";
        Click(teacher, "CalculatorEvaluateButton");
        Assert(Find<TextBlock>(teacher, "Status").Text == "Result 0.5.");
        Pump();
        Assert(Pixel(student, 620, 470).SequenceEqual(paper), "private calculation leaked");
        Check(teacher, "PublishInstrument", true);
        Pump();
        Assert(!Pixel(student, 620, 470).SequenceEqual(paper));
        Click(teacher, "RemoveInstrumentButton");
        Pump();
        Assert(Pixel(student, 620, 470).SequenceEqual(paper));
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
        var drag = BoardSelection.Begin([ink], null, new Point(120, 100), 1)!;
        teacher.MarkManipulation(ink, drag.Preview(new Point(320, 200)));
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

        var moved = (Ink)drag.Commit(new Point(320, 200));
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

    private static BitmapSource SolidFrame(Color color)
    {
        var pixels = new byte[16 * 9 * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
        }
        var image = BitmapSource.Create(16, 9, 96, 96, PixelFormats.Bgr24, null, pixels, 16 * 3);
        image.Freeze();
        return image;
    }

    private static void StudioComposition()
    {
        var profile = RecordingProfile.Options[^1];
        var renderer = new ProgramVideoRenderer(profile);
        var layers = new ProgramLayers(SolidFrame(Colors.Red), SolidFrame(Colors.Blue),
            new(ClockView.Analog, TimeSpan.FromHours(10)),
            [new(Guid.NewGuid(), false, new(InstrumentPose.Create(InstrumentKind.Ruler)))]);
        var frame = new StudioFrame(ProgramFrame.From(Page.Empty()), null, layers);
        var pixels = renderer.Capture(frame);
        Assert(pixels.Length == 3840 * 2160 * 4);
        int Channel(int x, int y, int c) => pixels[(y * 3 * profile.Width + x * 3) * 4 + c];
        Assert(Channel(980, 560, 2) > 220 && Channel(980, 560, 0) < 20, "camera one was not rendered at UHD");
        Assert(Channel(660, 560, 0) > 220 && Channel(660, 560, 2) < 20, "camera two was not rendered at UHD");
        Assert(Channel(1170, 70, 2) < 80, "clock overlay was not rendered");
        Assert(Channel(650, 416, 2) < 240 && Channel(650, 416, 2) > 120, "published ruler not rendered");
        renderer.Capture(frame with { Board = null });
        Assert(Channel(980, 560, 2) == 0 && Channel(660, 560, 0) == 0 && Channel(1170, 70, 2) == 0);
    }

    private static void MediaSmoke() => InTemporaryDirectory(directory =>
    {
        var ffmpeg = FfmpegMedia.Find() ?? throw new InvalidOperationException("FFmpeg must be on PATH for --media.");
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        Assert(File.Exists(ffprobe), "ffprobe.exe must be alongside FFmpeg.");
        var devices = FfmpegMedia.DevicesAsync(ffmpeg).GetAwaiter().GetResult();
        Console.WriteLine($"      FFmpeg enumerated {devices.Cameras.Count} video and {devices.Microphones.Count} audio inputs.");
        using var first = new CameraCapture(ffmpeg, ["-re", "-f", "lavfi", "-i", "color=c=red:s=640x360:r=15"]);
        using var second = new CameraCapture(ffmpeg, ["-re", "-f", "lavfi", "-i", "color=c=blue:s=640x360:r=15"]);
        var cameraOne = first.FirstFrame.WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
        var cameraTwo = second.FirstFrame.WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
        Assert(cameraOne.IsFrozen && cameraTwo.IsFrozen && cameraOne.PixelWidth == 640 && cameraTwo.PixelHeight == 360);
        var frame = new StudioFrame(ProgramFrame.From(Page.Empty()), null,
            new ProgramLayers(cameraOne, cameraTwo, new(ClockView.Digital, TimeSpan.FromHours(10))));

        foreach (var profile in RecordingProfile.Options)
        {
            var destination = Path.Combine(directory, $"check-{profile.Width}.mp4");
            var pixels = new ProgramVideoRenderer(profile).Capture(frame);
            var recording = new ProgramRecording(ffmpeg, destination, profile, () => Task.FromResult(pixels));
            Thread.Sleep(850);
            recording.Stop();
            Assert(recording.Completion.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult() == destination);
            using var probe = FfmpegMedia.Start(ffprobe,
                ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,width,height",
                    "-of", "json", destination], output: true);
            using var document = JsonDocument.Parse(probe.StandardOutput.ReadToEnd());
            Assert(probe.WaitForExit(5000) && probe.ExitCode == 0);
            var video = document.RootElement.GetProperty("streams")[0];
            Assert(video.GetProperty("codec_name").GetString() == "h264" &&
                video.GetProperty("width").GetInt32() == profile.Width &&
                video.GetProperty("height").GetInt32() == profile.Height);
            var x = (int)(1000d * profile.Width / BoardSurface.BoardWidth);
            var y = (int)(560d * profile.Height / BoardSurface.BoardHeight);
            using var decode = FfmpegMedia.Start(ffmpeg,
                ["-v", "error", "-i", destination, "-frames:v", "1", "-vf", $"crop=2:2:{x}:{y}",
                    "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1"], output: true);
            var decodedPixel = new byte[12];
            decode.StandardOutput.BaseStream.ReadExactly(decodedPixel);
            Assert(decode.WaitForExit(5000) && decode.ExitCode == 0 && decodedPixel[2] > 220 && decodedPixel[0] < 30);
            using var silence = FfmpegMedia.Start(ffprobe,
                ["-v", "error", "-select_streams", "a", "-show_entries", "stream=codec_name", "-of", "csv=p=0", destination],
                output: true);
            Assert(silence.StandardOutput.ReadToEnd().Length == 0 && silence.WaitForExit(5000) && silence.ExitCode == 0,
                "A recording without a selected microphone must contain no audio track.");
            Console.WriteLine($"      FFprobe verified H.264 at {profile.Width} × {profile.Height}.");
        }
        RecordingAudioSmoke(ffmpeg, ffprobe, directory);
    });

    private static void RecordingAudioSmoke(string ffmpeg, string ffprobe, string directory)
    {
        var destination = Path.Combine(directory, "check-audio.mp4");
        var profile = RecordingProfile.Options[0];
        var renderer = new ProgramVideoRenderer(profile);
        var black = new StudioFrame(null, null, new ProgramLayers());
        var darkPixels = renderer.Capture(black).ToArray();
        var lightPixels = renderer.Capture(black with { Board = ProgramFrame.From(Page.Empty()) });
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var recording = new ProgramRecording(ffmpeg, destination, profile,
            () => Task.FromResult(elapsed.ElapsedMilliseconds < 900 ? darkPixels : lightPixels),
            ["-re", "-f", "lavfi", "-i", "aevalsrc=0.25*sin(2*PI*440*t)*gte(t\\,0.9):s=48000"]);
        Thread.Sleep(2400);
        recording.Stop();
        Assert(recording.Completion.WaitAsync(TimeSpan.FromSeconds(25)).GetAwaiter().GetResult() == destination);

        using var probe = FfmpegMedia.Start(ffprobe,
            ["-v", "error", "-show_entries", "stream=codec_name,codec_type,sample_rate,duration", "-of", "json", destination],
            output: true);
        using var report = JsonDocument.Parse(probe.StandardOutput.ReadToEnd());
        Assert(probe.WaitForExit(5000) && probe.ExitCode == 0);
        var streams = report.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert(streams.Length == 2);
        var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video");
        var audio = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert(audio.GetProperty("codec_name").GetString() == "aac" && audio.GetProperty("sample_rate").GetString() == "48000");
        static double Duration(JsonElement stream) => double.Parse(stream.GetProperty("duration").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert(Duration(audio) > 2 && Duration(video) > 2 && Math.Abs(Duration(audio) - Duration(video)) < .2,
            "The MP4 audio and video tracks must finish together.");

        byte[] Decode(params string[] args)
        {
            using var process = FfmpegMedia.Start(ffmpeg, ["-v", "error", "-i", destination, .. args, "pipe:1"], output: true);
            using var buffer = new MemoryStream();
            try
            {
                process.StandardOutput.BaseStream.CopyToAsync(buffer).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                Assert(process.WaitForExit(5000) && process.ExitCode == 0);
                return buffer.ToArray();
            }
            finally { FfmpegMedia.Stop(process); }
        }

        var pictures = Decode("-an", "-vf", "crop=2:2:32:32", "-pix_fmt", "bgr24", "-f", "rawvideo");
        var samples = Decode("-vn", "-ac", "1", "-ar", "48000", "-f", "s16le");
        var lightFrame = Enumerable.Range(0, pictures.Length / 12).First(i => pictures[i * 12] > 100);
        var toneSample = Enumerable.Range(0, samples.Length / 2).First(i => Math.Abs((int)BitConverter.ToInt16(samples, i * 2)) > 2000);
        var lightTime = lightFrame / (double)RecordingProfile.Fps;
        var toneTime = toneSample / 48000d;
        Assert(lightTime is > .65 and < 1.2 && toneTime is > .65 and < 1.2 && Math.Abs(lightTime - toneTime) < .2,
            $"The generated tone and picture transition diverged: {toneTime:F3}s audio, {lightTime:F3}s video.");
        Console.WriteLine($"      FFprobe verified AAC. Test tone at {toneTime:F3}s and Program transition at {lightTime:F3}s.");

        var saved = File.ReadAllBytes(destination);
        var unavailable = new ProgramRecording(ffmpeg, destination, profile, () => Task.FromResult(lightPixels),
            new MicrophoneDevice("Unavailable test microphone", $"EduCast-test-{Guid.NewGuid():N}"));
        Expect<IOException>(() => unavailable.Completion.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult());
        Assert(File.ReadAllBytes(destination).SequenceEqual(saved), "A microphone failure replaced an existing recording.");
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
