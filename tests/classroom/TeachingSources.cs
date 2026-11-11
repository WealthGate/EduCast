using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EduCast.Classroom;
using EduCast.Studio;
using Microsoft.Web.WebView2.Wpf;
using Page = EduCast.Classroom.Page;
using Visibility = EduCast.Classroom.Visibility;

internal static partial class Checks
{
    private static void TeachingAddresses()
    {
        Assert(BrowserLesson.WebAddress(" example.org/lesson ")?.AbsoluteUri == "https://example.org/lesson");
        Assert(BrowserLesson.WebAddress("http://localhost:8080/lesson")?.Port == 8080);
        Assert(BrowserLesson.WebAddress("file:///C:/private.html") is null);
        Assert(BrowserLesson.WebAddress("javascript:alert(1)") is null);
        Assert(BrowserLesson.WebAddress("https://user:pass@example.org") is null);
        Assert(BrowserLesson.IsSupportedFile("lesson.HTML") && BrowserLesson.IsSupportedFile("chapter.pdf") &&
            BrowserLesson.IsSupportedFile("diagram.PNG") && BrowserLesson.IsSupportedFile("image.webp") &&
            !BrowserLesson.IsSupportedFile("lesson.educast"));
        Assert(TeachingFile.Classify("lesson.EDUCAST") == TeachingFileKind.Board);
        Assert(TeachingFile.Classify("interactive #1 (1).HTML") == TeachingFileKind.Browser);
        Assert(TeachingFile.Classify("clip.MP4") == TeachingFileKind.Media);
        Assert(TeachingFile.Classify("lab.PPSX") == TeachingFileKind.DesktopApp);
        Assert(TeachingFile.Classify("model.ggb") == TeachingFileKind.DesktopApp);
        Assert(TeachingFile.LocalPath(" \"C:\\Lesson Files\\interactive #1 (1).html\" ") == @"C:\Lesson Files\interactive #1 (1).html");
        Assert(TeachingFile.LocalPath("file:///C:/Lesson%20Files/interactive%20%231.html") == @"C:\Lesson Files\interactive #1.html");
        Assert(TeachingFile.LocalPath("https://example.org/lesson") is null);
    }

    private static int CaptureFixture(string title)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var surface = new Border { Background = Brushes.DeepSkyBlue };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => surface.Background = surface.Background == Brushes.DeepSkyBlue
            ? Brushes.DodgerBlue : Brushes.DeepSkyBlue;
        timer.Start();
        var window = new Window
        {
            Title = title, Width = 640, Height = 400, Content = surface, Topmost = true,
            Left = SystemParameters.WorkArea.Left, Top = SystemParameters.WorkArea.Top
        };
        return new Application().Run(window);
    }

    private static void SourceComposition()
    {
        var arrow = new Shape(Guid.NewGuid(), Visibility.Both, false, 0xffe50000, 8,
            ShapeKind.Arrow, new(100, 180), new(230, 180));
        Assert(BoardSurface.Hit([arrow], new Point(220, 174)) == arrow);
        var ink = new Ink(Guid.NewGuid(), Visibility.Both, false, 0xffe50000, 8, false, false,
            [new(120, 250), new(220, 250)], InkTip.Square);
        var source = new SourceFrame(SolidFrame(Colors.DodgerBlue), new SourceInk([arrow, ink], Pointer: new Point(310, 310)));
        var board = ProgramFrame.From(Page.Empty());
        var frame = new StudioFrame(board, null, new ProgramLayers(Source: source));
        var renderer = new ProgramVideoRenderer(RecordingProfile.Options[0]);
        var pixels = renderer.Capture(frame);
        int Offset(int x, int y) => (y * 1280 + x) * 4;
        Assert(pixels[Offset(20, 20)] == Colors.DodgerBlue.B);
        Assert(pixels[Offset(170, 180) + 2] > 200 && pixels[Offset(170, 250) + 2] > 200);
        Assert(pixels[Offset(320, 321) + 2] > 200);
        using var png = new MemoryStream();
        ProgramImage.WritePng(frame, png);
        png.Position = 0;
        var still = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert(Pixel(still, 170, 180)[2] > 200);
        Assert(Pixel(still, 320, 321)[0] == Colors.DodgerBlue.B);
        Assert(renderer.Capture(frame with { Board = null }).All(pixel => pixel is 0 or 255));
    }

    private static void TeachingSources(Application app, TeacherWindow teacher, FrameworkElement program, string lesson)
    {
        var boardPixel = Pixel(program, 25, 25);
        Click(teacher, "BrowserButton");
        var source = app.Windows.OfType<SourceWindow>().Single();
        var publish = Find<CheckBox>(source, "Publish");
        var hint = Find<TextBlock>(source, "Hint");
        var slot = Find<Grid>(source, "BrowserSlot");
        source.Dispatcher.BeginInvoke(() => teacher.OpenSelectedFile(lesson));
        WaitFor(() => publish.IsEnabled, () => hint.Text);
        Assert(Find<TextBox>(source, "Address").Text == Path.GetFullPath(lesson),
            "a local lesson must show its real file path");
        Assert(Pixel(program, 25, 25).SequenceEqual(boardPixel), "a private browser must not reach Program");
        var browser = (WebView2CompositionControl)slot.Children[0];
        var screenOrigin = browser.PointToScreen(new Point());
        var screenEdge = browser.PointToScreen(new Point(browser.ActualWidth, 0));
        Assert(Math.Abs(screenEdge.X - screenOrigin.X - browser.ActualWidth * VisualTreeHelper.GetDpi(browser).DpiScaleX) < 2,
            "The WPF layout must leave the live browser at native size so mouse input aligns.");
        var script = browser.CoreWebView2.ExecuteScriptAsync("""
            document.querySelector('[data-tab="1"]').click();
            const slider = document.querySelector('#theta');
            slider.value = '110';
            slider.dispatchEvent(new Event('input', {bubbles: true}));
            document.querySelector('#thetaval').textContent.includes('110') && !!document.querySelector('svg line');
            """);
        WaitFor(() => script.IsCompleted, () => "the interactive lesson script timed out");
        Assert(script.GetAwaiter().GetResult() == "true", "the lesson's vector controls did not update");
        void Publish(bool value)
        {
            publish.IsChecked = value;
            publish.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Pump();
        }

        Publish(true);
        WaitFor(() => !Pixel(program, 25, 25).SequenceEqual(boardPixel), () => "the browser did not reach Program");
        var clean = Pixel(program, 195, 200);
        Click(source, "SquarePen");
        var input = Find<LiveInkCanvas>(source, "Input");
        var stroke = new Stroke(new StylusPointCollection([new StylusPoint(190, 200), new StylusPoint(210, 200)]),
            input.DefaultDrawingAttributes);
        input.Strokes.Add(stroke);
        input.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(stroke) { RoutedEvent = InkCanvas.StrokeCollectedEvent });
        Pump();
        Assert(Pixel(program, 195, 200)[2] > 225 && !Pixel(program, 195, 200).SequenceEqual(clean),
            "the browser annotation must reach Program");
        Click(source, "SelectAnnotation");
        var drawing = Find<Grid>(source, "Drawing");
        var presentation = PresentationSource.FromVisual(source);
        source.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, presentation, 0, Key.F4)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert(!drawing.IsHitTestVisible && !Find<TeacherInstrumentSurface>(source, "SourceInstruments").IsHitTestVisible,
            "F4 must let the browser receive pointer input through the annotation layer");
        source.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, presentation, 0, Key.F4)
            { RoutedEvent = Keyboard.PreviewKeyUpEvent });
        Assert(drawing.IsHitTestVisible && input.EditingMode == InkCanvasEditingMode.None,
            "releasing F4 must return to selection");
        teacher.ReturnToBoard();
        Pump();
        Assert(!source.IsVisible && publish.IsChecked == true && Pixel(program, 25, 25).SequenceEqual(boardPixel));
        Click(teacher, "ResumeSourceButton");
        Assert(source.IsVisible && Pixel(program, 195, 200)[2] > 225, "resuming must keep browser annotations");
        teacher.SourceFreeze();
        Click(source, "ClearButton");
        Assert(Pixel(program, 195, 200)[2] > 225, "freeze must hold the source marks");
        Publish(false);
        Assert(Pixel(program, 25, 25).SequenceEqual(boardPixel), "unsharing must revoke a held source");
        teacher.SourceFreeze();
        CheckBrowserAddress(source, browser);

        var title = $"EduCast capture check {Guid.NewGuid():N}";
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--capture-fixture");
        info.ArgumentList.Add(title);
        using var target = Process.Start(info)!;
        try
        {
            WaitFor(() => CaptureWindow.List().Any(window => window.Title == title), () => "the capture fixture did not open");
            Click(teacher, "WindowCaptureButton");
            var windows = Find<ComboBox>(source, "Windows");
            Assert(windows.Items.OfType<CaptureWindow>().All(window => window.ProcessId != Environment.ProcessId));
            windows.SelectedItem = windows.Items.OfType<CaptureWindow>().Single(window => window.Title == title);
            Click(source, "PreviewWindowButton");
            WaitFor(() => publish.IsEnabled, () => hint.Text);
            var interact = Find<Button>(source, "Interact");
            Assert(interact.IsEnabled && Equals(interact.Content, "Interact in app"));
            Assert(Pixel(program, 25, 25).SequenceEqual(boardPixel), "a private capture must not reach Program");
            var live = Find<CheckBox>(teacher, "LiveMode");
            live.IsChecked = false;
            live.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Publish(true);
            var capturePreview = Find<SourceSurface>(source, "Surface");
            static bool IsFixtureBlue(byte[] pixel) => pixel[0] > 200 && pixel[1] > 140 && pixel[2] < 40;
            WaitFor(() => IsFixtureBlue(Pixel(capturePreview, 640, 360)), () =>
            {
                var raw = source.PublishedFrame(false)?.Image;
                return raw is null ? hint.Text : $"Captured {raw.PixelWidth}×{raw.PixelHeight} " +
                    $"top left BGRA {string.Join(',', Pixel(raw, 8, 8))}, " +
                    $"center BGRA {string.Join(',', Pixel(raw, raw.PixelWidth / 2, raw.PixelHeight / 2))}.";
            });
            Assert(Pixel(program, 25, 25).SequenceEqual(boardPixel), "manual mode must wait for Take");
            teacher.SourceTake();
            Pump();
            var center = Pixel(program, 640, 360);
            Assert(IsFixtureBlue(center), $"the chosen window did not reach Program. BGRA {string.Join(',', center)}.");
            teacher.SourceBlackout();
            Pump();
            Assert(Pixel(program, 640, 360).Take(3).All(channel => channel == 0));
            teacher.SourceBlackout();
            Assert(target.CloseMainWindow());
            WaitFor(() => publish.IsChecked == false, () => "closing the original window did not revoke the capture");
            Assert(Pixel(program, 25, 25).SequenceEqual(boardPixel));
            live.IsChecked = true;
            live.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        }
        finally
        {
            if (!target.HasExited)
                target.Kill();
            source.Close();
        }
    }

    private static void CheckBrowserAddress(SourceWindow source, WebView2CompositionControl browser)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var request = await listener.AcceptTcpClientAsync();
            using var stream = request.GetStream();
            using var headers = new StreamReader(stream);
            while (await headers.ReadLineAsync() is { Length: > 0 }) { }
            const string html = "<!doctype html><title>EduCast URL check</title><p>Local web address loaded.</p>";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {html.Length}\r\nConnection: close\r\n\r\n{html}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
        });
        Find<TextBox>(source, "Address").Text = $"http://127.0.0.1:{port}/lesson";
        source.Dispatcher.BeginInvoke(() => Click(source, "GoButton"));
        WaitFor(() => browser.CoreWebView2.DocumentTitle == "EduCast URL check" && Find<CheckBox>(source, "Publish").IsEnabled,
            () => $"the typed address did not open. {Find<TextBlock>(source, "Hint").Text}");
        serve.GetAwaiter().GetResult();
    }

    private static void WaitFor(Func<bool> condition, Func<string> error)
    {
        var wait = Stopwatch.StartNew();
        while (!condition() && wait.Elapsed < TimeSpan.FromSeconds(20))
        {
            Pump();
            Thread.Sleep(20);
        }
        Assert(condition(), error());
    }
}
