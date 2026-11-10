using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EduCast.Classroom;
using EduCast.Studio;
using Page = EduCast.Classroom.Page;
using Visibility = EduCast.Classroom.Visibility;

internal static partial class Checks
{
    private static Ink SketchInk(params BoardPoint[] points) =>
        new(Guid.NewGuid(), Visibility.TeacherOnly, true, InkColor, 4, true, false, [.. points]);

    private static Ink GlyphInk(string points, double x, double y, double scale) =>
        SketchInk(points.Split(' ').Select(point =>
            new BoardPoint(x + (point[0] - '0') * scale, y + (point[1] - '0') * scale)).ToArray());

    private static void SmartNotation()
    {
        var two = GlyphInk("02 20 60 81 83 65 29 89", 50, 20, 3);
        var three = GlyphInk("01 60 81 83 55 35 65 87 88 69 29 08", 52, 85, 3);
        var bar = SketchInk(new(35, 66), new(105, 66));
        var fraction = MathInkRecognition.Recognize([two, three, bar]);
        Assert(fraction is { Count: 3, Math.Source: "(2)/(3)" }, $"fraction recognized as {fraction?.Math.Source}");
        Assert(MathParser.TryParse(fraction!.Value.Math.Source, out var parsed, out _) && parsed is Fraction);

        var dots = MathInkRecognition.Recognize([SketchInk(new BoardPoint(72, 50)), bar, SketchInk(new BoardPoint(70, 84))]);
        Assert(dots is { Count: 3, Math: { Source: "÷", IsSymbol: true } });

        var x = GlyphInk("00 99", 70, 96, 4);
        var cross = GlyphInk("90 09", 70, 96, 4);
        var exponent = GlyphInk("02 20 60 81 83 65 29 89", 111, 78, 2.5);
        var index = SketchInk(new(112, 125), new(112, 150));
        var power = MathInkRecognition.Recognize([x, cross, exponent]);
        Assert(power is { Count: 3, Math.Source: "x^(2)" }, $"power recognized as {power?.Math.Source}");
        var subscript = MathInkRecognition.Recognize([x, cross, index]);
        Assert(subscript is { Count: 3, Math.Source: "x_(1)" }, $"subscript recognized as {subscript?.Math.Source}");
        var scripts = MathInkRecognition.Recognize([x, cross, index, exponent]);
        Assert(scripts is { Count: 4, Math.Source: "x_(1)^(2)" }, $"scripts recognized as {scripts?.Math.Source}");

        var hook = SketchInk(new(110, 151), new(120, 167), new(139, 110), new(213, 110));
        var term = GlyphInk("02 20 60 81 83 65 29 89", 151, 121, 4);
        var root = MathInkRecognition.Recognize([hook, term]);
        Assert(root is { Count: 2, Math.Source: "sqrt(2)" }, $"root recognized as {root?.Math.Source}");
        var separateRoof = MathInkRecognition.Recognize([SketchInk(new(110, 151), new(120, 167), new(139, 110)),
            SketchInk(new(138, 110), new(213, 110)), term]);
        Assert(separateRoof is { Count: 3, Math.Source: "sqrt(2)" });
        var cubeIndex = GlyphInk("01 60 81 83 55 35 65 87 88 69 29 08", 97, 112, 2.25);
        var cube = MathInkRecognition.Recognize([cubeIndex, hook, term]);
        Assert(cube is { Count: 3, Math.Source: "root(3,2)" }, $"cube root recognized as {cube?.Math.Source}");
        Assert(MathInkRecognition.Recognize([hook]) is { Math.Source: "sqrt(?)" });
        Assert(MathInkRecognition.Recognize([x, cross]) is null);
    }

    private static void SolidSketches()
    {
        for (var sides = 5; sides <= 12; sides++)
        {
            var points = Enumerable.Range(0, sides + 1)
                .Select(i => InstrumentPose.Polar(i * 360d / sides - 90, 75))
                .Select(point => new BoardPoint(200 + point.X, 200 + point.Y)).ToArray();
            var polygon = SketchRecognition.Recognize(SketchInk(points), null, BoardSubject.Mathematics);
            Assert(polygon is { ShapeKind: ShapeKind.Polygon, Vertices: { } } && polygon.Vertices.Value.Length == sides,
                $"expected {sides} sides, got {polygon?.ShapeKind} ({polygon?.Vertices?.Length})");
            var moved = (Shape)BoardSurface.Move(polygon!, new Vector(25, 10));
            Assert(moved.Vertices!.Value[0].X == polygon!.Vertices!.Value[0].X + 25);
        }

        Ink Ellipse(double x, double y, double rx, double ry) => SketchInk(Enumerable.Range(0, 49)
            .Select(i => new BoardPoint(x + rx * Math.Cos(i * Math.Tau / 48), y + ry * Math.Sin(i * Math.Tau / 48))).ToArray());
        var baseEllipse = Ellipse(150, 220, 62, 17);
        var a = SketchInk(new(88, 220), new(151, 94));
        var b = SketchInk(new(151, 94), new(212, 220));
        var cone = SketchRecognition.RecognizeGroup([a, baseEllipse, b], BoardSubject.Mathematics);
        Assert(cone is { Count: 3, Shape: { ShapeKind: ShapeKind.Cone, Visibility: Visibility.TeacherOnly, IsAnswer: true } });
        Assert(SketchRecognition.RecognizeGroup([baseEllipse, SketchInk(new(88, 220), new(151, 94), new(212, 220))],
            BoardSubject.General) is { Count: 2, Shape.ShapeKind: ShapeKind.Cone });
        Assert(SketchRecognition.RecognizeGroup([Ellipse(200, 200, 60, 60), Ellipse(200, 200, 60, 17)],
            BoardSubject.General) is { Count: 2, Shape.ShapeKind: ShapeKind.Sphere });

        foreach (var kind in new[] { ShapeKind.Cone, ShapeKind.Sphere, ShapeKind.Cube, ShapeKind.Cuboid, ShapeKind.Cylinder })
        {
            var geometry = BoardSurface.ShapeGeometry(new Shape(Guid.NewGuid(), Visibility.Both, false, InkColor, 4, kind,
                new(60, 90), new(240, 220)));
            Assert(geometry.Bounds.Width >= 125 && geometry.Bounds.Height >= 125, kind.ToString());
        }

        var vertices = new[] { new BoardPoint(120, 100), new(200, 140), new(220, 220), new(100, 225) };
        var polygonObject = new Shape(Guid.NewGuid(), Visibility.Both, false, InkColor, 4, ShapeKind.Polygon,
            new(100, 100), new(220, 225)) { Vertices = [.. vertices] };
        InTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "solids.educast");
            var page = Page.Empty() with { Items = [polygonObject, cone!.Value.Shape] };
            LessonFile.Save(path, new Lesson(1, "Geometry", [page]));
            var reloaded = LessonFile.Load(path).Pages[0];
            Assert(reloaded.Items[0] is Shape { Vertices: { } corners } && corners.SequenceEqual(vertices));
            Assert(ProgramFrame.From(reloaded).Items.Length == 1, "private solid must not reach Program");
        });
    }

    private static void InstrumentClearance()
    {
        BitmapSource Draw(InstrumentPose pose, bool controls)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                InstrumentDrawing.Draw(drawing, new InstrumentDisplay(pose), controls);
            var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }

        foreach (var kind in new[] { InstrumentKind.Ruler, InstrumentKind.Compass,
            InstrumentKind.Protractor180, InstrumentKind.Protractor360 })
        {
            var pose = InstrumentPose.Create(kind);
            var editing = Draw(pose, controls: true);
            var clean = Draw(pose, controls: false);
            var references = kind switch
            {
                InstrumentKind.Ruler or InstrumentKind.Compass => new[] { new BoardPoint(0, 0), new BoardPoint(pose.Size, 0) },
                _ => new[] { new BoardPoint(0, 0), InstrumentPose.Polar(-pose.Angle, pose.Size - 27),
                    InstrumentPose.Polar(-pose.Angle, pose.Size - 7) }
            };
            foreach (var reference in references.Select(pose.ToBoard))
                for (var dy = -5; dy <= 5; dy++)
                    for (var dx = -5; dx <= 5; dx++)
                        Assert(Pixel(editing, (int)reference.X + dx, (int)reference.Y + dy)
                            .SequenceEqual(Pixel(clean, (int)reference.X + dx, (int)reference.Y + dy)), kind.ToString());
        }
    }
}
