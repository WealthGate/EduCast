using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EduCast.Classroom;
using EduCast.Studio;

internal static partial class Checks
{
    private static void ToolbarRows()
    {
        var toolbar = new ToolbarPanel();
        foreach (var width in new[] { 95, 74, 155, 88, 62, 108, 66 })
            toolbar.Children.Add(new Border { MinWidth = width, Height = 35 });
        toolbar.Children.Add(new Border { MinWidth = 400, Visibility = System.Windows.Visibility.Collapsed });

        foreach (var width in new[] { 320, 620, 1360 })
        {
            toolbar.Measure(new Size(width, double.PositiveInfinity));
            toolbar.Arrange(new Rect(0, 0, width, toolbar.DesiredSize.Height));
            CheckToolbarRows(toolbar);
        }

        var shape = new Shape(Guid.NewGuid(), EduCast.Classroom.Visibility.Both, false, InkColor, 4,
            ShapeKind.Rectangle, new(80, 70), new(240, 200));
        var center = CanvasClose.Center(new Rect(72, 62, 176, 146), 1);
        Assert(BoardSelection.CloseAt(shape, center, 1), "selection close button could not be hit");
        Assert(BoardSelection.Hover(shape, center, 1) == System.Windows.Input.Cursors.Hand);
    }

    private static void CheckToolbarRows(ToolbarPanel toolbar)
    {
        var rows = toolbar.Children.Cast<FrameworkElement>()
            .Where(item => item.Visibility != System.Windows.Visibility.Collapsed)
            .GroupBy(item => Math.Round(LayoutInformation.GetLayoutSlot(item).Top, 1));
        foreach (var row in rows)
        {
            var last = 0d;
            foreach (var item in row)
            {
                var slot = LayoutInformation.GetLayoutSlot(item);
                Assert(Math.Abs(slot.Left - last) < .1 && slot.Right <= toolbar.ActualWidth + .1,
                    "toolbar controls overlap or exceed the available row width");
                last = slot.Right;
            }
            Assert(Math.Abs(last - toolbar.ActualWidth) < .1, "toolbar row leaves unused space at its end");
        }
    }

    private static void AppearanceChecks(App app, TeacherWindow teacher, FrameworkElement program, string path)
    {
        var paper = Pixel(program, 200, 200);
        var picker = Find<ComboBox>(teacher, "ThemePicker");
        foreach (var theme in Enum.GetValues<WorkspaceTheme>())
        {
            picker.SelectedItem = theme;
            Pump();
            Assert(app.Appearance.Selected == theme);
            Assert(((SolidColorBrush)teacher.Background).Color == ((SolidColorBrush)app.Resources["Shell"]).Color);
            Assert(((SolidColorBrush)Find<Button>(teacher, "PenButton").Background).Color ==
                ((SolidColorBrush)app.Resources["AccentDark"]).Color, "selected tools did not change theme");
            Assert(Pixel(program, 200, 200).SequenceEqual(paper), "workspace theme changed the student paper");
        }

        Assert(File.ReadAllText(path) == nameof(WorkspaceTheme.Daylight));
        var reloaded = new Appearance(app, path);
        reloaded.Load();
        Assert(reloaded.Selected == WorkspaceTheme.Daylight);
        File.WriteAllText(path, "999");
        reloaded.Load();
        Assert(reloaded.Selected == WorkspaceTheme.Daylight, "an invalid saved theme changed the palette");
        app.Appearance.Selected = WorkspaceTheme.Violet;
        CheckToolbarRows(Find<ToolbarPanel>(teacher, "BoardTools"));
    }

    private static void CaptureLayouts(App app, TeacherWindow teacher, SourceWindow source, string[] args)
    {
        var index = Array.IndexOf(args, "--capture-layouts");
        if (index < 0 || index + 1 >= args.Length)
            return;
        var directory = args[index + 1];
        Directory.CreateDirectory(directory);
        var teacherSize = new Size(teacher.Width, teacher.Height);
        var sourceSize = new Size(source.Width, source.Height);
        try
        {
            foreach (var theme in Enum.GetValues<WorkspaceTheme>())
            {
                app.Appearance.Selected = theme;
                teacher.Width = 1920;
                teacher.Height = 1080;
                teacher.OpenInstruments(source: false);
                Pump();
                SaveView(teacher, Path.Combine(directory, $"board-{theme}.png"));
                teacher.OpenInstruments(source: true);
                source.Width = 1100;
                source.Height = 790;
                Pump();
                SaveView(source, Path.Combine(directory, $"source-{theme}.png"));
            }

            teacher.Width = teacher.MinWidth;
            teacher.Height = teacher.MinHeight;
            teacher.OpenInstruments(source: false);
            Pump();
            Assert(Find<ScrollViewer>(teacher, "BoardToolbar").ActualHeight <= teacher.ActualHeight * .6);
            Assert(Find<ScrollViewer>(teacher, "BoardScroller").ActualHeight > 100, "narrow toolbar covered the board");
            CheckToolbarRows(Find<ToolbarPanel>(teacher, "InstrumentTools"));
            SaveView(teacher, Path.Combine(directory, "board-narrow.png"));
            teacher.OpenInstruments(source: true);
            source.Width = source.MinWidth;
            source.Height = source.MinHeight;
            Pump();
            Assert(Find<FrameworkElement>(source, "SourceStage").ActualHeight > 60, "narrow toolbar covered the source");
            CheckToolbarRows(Find<ToolbarPanel>(source, "AnnotationTools"));
            SaveView(source, Path.Combine(directory, "source-narrow.png"));
        }
        finally
        {
            teacher.Width = teacherSize.Width;
            teacher.Height = teacherSize.Height;
            source.Width = sourceSize.Width;
            source.Height = sourceSize.Height;
            app.Appearance.Selected = WorkspaceTheme.Violet;
            teacher.SetMathSurface(source: true);
        }
    }

    private static void SaveView(Window window, string path)
    {
        var view = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),
            (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight);
            drawing.DrawRectangle(window.Background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(view), null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
