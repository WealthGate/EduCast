using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using EduCast.Classroom;
using EduCast.Studio;
using Visibility = EduCast.Classroom.Visibility;

internal static partial class Checks
{
    private const string TwoFrameGif = "R0lGODlhCgAGAIEAAAAAAP8AAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQJCgAAACwAAAAACgAGAAAIGAABCBxIEECAgwUFHgyQ0CDChAsbSgQQEAAh+QQJFwAAACwAAAAACgAGAIEAAAAAAP8AAAAAAAAIFwABCBxIsGCAgwUFHgyQEMDChg8bEgwIADs=";

    private static void CanvasMedia()
    {
        var ticker = CanvasTextEditor.New(new Point(110, 360), 0xff182735, Visibility.Both) with
        {
            Text = "Next experiment",
            Style = new BoardTextStyle("Segoe UI", 620, Bold: true, Italic: true, Underline: true,
                Case: TextCase.Uppercase, Align: TextAlign.Center, Scroll: TextScroll.Left, Speed: 140)
        };
        var gif = new EffectAsset("Animated effect", TwoFrameGif).Place(new BoardPoint(80, 70), Visibility.Both);
        var privateEffect = gif with { Id = Guid.NewGuid(), Visibility = Visibility.TeacherOnly, Origin = new BoardPoint(650, 70) };
        var page = EduCast.Classroom.Page.Empty() with { Items = [ticker, gif, privateEffect] };
        Assert(EffectMedia.For(gif.Data) is { Animated: true }, "the imported GIF must decode as animation");
        var first = EffectMedia.For(gif.Data).At(0)!;
        var second = EffectMedia.For(gif.Data).At(.16)!;
        Assert(Pixel(first, 2, 2)[2] > 245 && Pixel(second, 2, 2)[3] == 0 && Pixel(second, 6, 2)[0] > 245,
            "GIF transparency and frame timing must be preserved");

        InTemporaryDirectory(folder =>
        {
            var path = Path.Combine(folder, "effects.educast");
            LessonFile.Save(path, Lesson.Empty() with { Pages = [page] });
            var opened = LessonFile.Load(path);
            Assert(opened.Pages[0].Items.SequenceEqual(page.Items), "text formatting and effect image data must survive save and open");
        });

        var projected = ProgramFrame.From(page);
        Assert(projected.Items.SequenceEqual([ticker, gif]), "teacher-only effects must stay out of Program");
        var renderer = new ProgramVideoRenderer(RecordingProfile.Options[0]);
        var frame = new StudioFrame(projected, null, new ProgramLayers());
        var initial = renderer.Capture(frame).ToArray();
        var moved = renderer.Capture(frame with { MotionTime = .16 });
        Assert(!initial.SequenceEqual(moved), "recording must render the next GIF and ticker frame");
        var tickerRow = 385 * 1280 * 4;
        Assert(!initial.AsSpan(tickerRow, 1280 * 4).SequenceEqual(moved.AsSpan(tickerRow, 1280 * 4)),
            "the scrolling text must advance independently of the GIF");
        Assert(moved[(136 * 1280 + 270) * 4] > 235);

        using var snapshot = new MemoryStream();
        ProgramImage.WritePng(frame with { MotionTime = .16 }, snapshot);
        snapshot.Position = 0;
        var exported = BitmapFrame.Create(snapshot, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert(Pixel(exported, 270, 136)[0] > 235 && Pixel(exported, 270, 136)[2] < 20,
            "a snapshot must capture the displayed GIF frame");

        var requests = 0;
        using var client = new HttpClient(new UpdateHttp(uri =>
        {
            requests++;
            return uri.AbsolutePath == "/start"
                ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/effect", UriKind.Relative) } }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String(TwoFrameGif)) };
        }));
        Assert(EffectMedia.Download(client, "https://example.test/start", default).GetAwaiter().GetResult() == TwoFrameGif &&
            requests == 2, "a direct download must follow HTTPS redirects and validate the image");
        Expect<InvalidDataException>(() => EffectMedia.Download(client, "http://example.test/effect", default)
            .GetAwaiter().GetResult());
        Assert(requests == 2);
    }

    private static Button EnterCanvasText(Window window, string overlay, string toolbar)
    {
        var box = Find<Canvas>(window, overlay).Children.OfType<TextBox>().Single();
        Assert(box.Visibility == System.Windows.Visibility.Visible && box.AcceptsReturn);
        box.Text = "Teacher notes\nSecond line";
        var tools = Find<StackPanel>(window, toolbar).Children.OfType<ToolbarPanel>().Single();
        tools.Children.OfType<TextBox>().Single().Text = "#A22143";
        tools.Children.OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == "Scrolling text").SelectedIndex = 1;
        tools.Children.OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == "Capitalization").SelectedIndex = 1;
        return tools.Children.OfType<Button>().Single(control => Equals(control.Content, "Done"));
    }
}
