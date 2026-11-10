using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using EduCast.Classroom;
using ItemVisibility = EduCast.Classroom.Visibility;
using Matrix = System.Windows.Media.Matrix;

namespace EduCast.Studio;

internal sealed class BoardSurface : FrameworkElement
{
    public const double BoardWidth = 1280;
    public const double BoardHeight = 720;
    public static readonly Rect Bounds = new(0, 0, BoardWidth, BoardHeight);
    private static readonly ConditionalWeakTable<Content, DrawingGroup> Drawings = new();
    private static (BoardPaper Paper, uint? Color, DrawingGroup Drawing)? lastPaper;
    private readonly DrawingVisual board = new();
    private readonly DrawingVisual overlay = new();

    public BoardSurface()
    {
        Width = BoardWidth;
        Height = BoardHeight;
        IsHitTestVisible = false;
        AddVisualChild(board);
        AddVisualChild(overlay);
    }

    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => board, 1 => overlay, _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public void Present(ProgramFrame? frame)
    {
        using var dc = board.RenderOpen();
        if (frame is null)
        {
            dc.DrawRectangle(Brushes.Black, null, Bounds);
            return;
        }
        Draw(dc, frame.Paper, frame.Background, frame.Items);
    }

    public void Edit(Page page)
    {
        using var dc = board.RenderOpen();
        Draw(dc, page.Paper, page.Background,
            page.Items.Where(item => item.Visibility is ItemVisibility.Both or ItemVisibility.TeacherOnly));
    }

    private static void Draw(DrawingContext dc, BoardPaper paper, uint? color, IEnumerable<Content> items)
    {
        dc.PushClip(new RectangleGeometry(Bounds));
        if (lastPaper is not { } cached || cached.Paper != paper || cached.Color != color)
            lastPaper = cached = (paper, color, MakePaper(paper, color));
        dc.DrawDrawing(cached.Drawing);
        foreach (var item in items)
            dc.DrawDrawing(For(item));
        dc.Pop();
    }

    public void Mark(Content? selected, Shape? ghost = null, double handleUnit = 1)
    {
        using var dc = overlay.RenderOpen();
        dc.PushClip(new RectangleGeometry(Bounds));
        if (ghost is not null)
            dc.DrawDrawing(For(ghost));
        if (selected is not null)
            BoardSelection.Draw(dc, BoundsOf(selected), handleUnit);
        dc.Pop();
    }

    public void MarkManipulation(Content item, Matrix transform, double handleUnit = 1)
    {
        using var dc = overlay.RenderOpen();
        dc.PushClip(new RectangleGeometry(Bounds));
        DrawManipulation(dc, item, transform, handleUnit);
        dc.Pop();
    }

    internal static void DrawManipulation(DrawingContext dc, Content item, Matrix transform, double handleUnit)
    {
        var drawing = For(item);
        dc.PushTransform(new MatrixTransform(transform));
        dc.DrawDrawing(drawing);
        dc.Pop();
        BoardSelection.Draw(dc, Rect.Transform(drawing.Bounds, transform), handleUnit);
    }

    public void PresentInk(ProgramInk? ink)
    {
        using var dc = overlay.RenderOpen();
        if (ink is null)
            return;
        dc.PushClip(new RectangleGeometry(Bounds));
        dc.DrawDrawing(For(ink.Stroke));
        dc.Pop();
    }

    public static Content? Hit(Page page, Point point) => Hit(page.Items, point);

    internal static Content? Hit(IEnumerable<Content> items, Point point) => items.Reverse()
        .Where(item => item.Visibility is ItemVisibility.Both or ItemVisibility.TeacherOnly)
        .FirstOrDefault(item =>
        {
            var scale = item.Placement?.Scale ?? 1;
            var local = item.Placement is { } placement
                ? new Point((point.X - placement.X) / scale, (point.Y - placement.Y) / scale) : point;
            if (item is Ink ink)
                return ToStroke(ink).HitTest(local, 14 / scale);
            if (item is Shape shape)
                return ShapeGeometry(shape).StrokeContains(new Pen(Brushes.Black, shape.Width + 14 / scale), local);
            var bounds = For(item).Bounds;
            bounds.Inflate(7, 7);
            return bounds.Contains(point);
        });

    public static Content Move(Content item, Vector offset)
    {
        var constrained = ConstrainMove(For(item).Bounds, offset);
        if (constrained.X == 0 && constrained.Y == 0)
            return item;
        if (item.Placement is { } placement)
            return item with { Placement = placement with { X = placement.X + constrained.X, Y = placement.Y + constrained.Y } };

        BoardPoint Shift(BoardPoint point) => point with { X = point.X + constrained.X, Y = point.Y + constrained.Y };
        return item switch
        {
            Ink ink => ink with { Points = ink.Points.Select(Shift).ToImmutableArray() },
            Shape shape => shape with
            {
                Start = Shift(shape.Start), End = Shift(shape.End), Corner = shape.Corner is { } corner ? Shift(corner) : null,
                Vertices = shape.Vertices?.Select(Shift).ToImmutableArray()
            },
            Equation equation => equation with { Origin = Shift(equation.Origin) },
            BoardText text => text with { Origin = Shift(text.Origin) },
            Graph graph => graph with { Origin = Shift(graph.Origin) },
            ChemicalEquation equation => equation with { Origin = Shift(equation.Origin) },
            TeachingDiagram diagram => diagram with { Origin = Shift(diagram.Origin) },
            _ => item
        };
    }

    internal static Rect BoundsOf(Content item) => For(item).Bounds;

    internal static Vector ConstrainMove(Rect drawingBounds, Vector offset) => drawingBounds.IsEmpty
        ? new Vector()
        : new Vector(
            Math.Clamp(offset.X, Math.Min(0, -drawingBounds.Left), Math.Max(0, BoardWidth - drawingBounds.Right)),
            Math.Clamp(offset.Y, Math.Min(0, -drawingBounds.Top), Math.Max(0, BoardHeight - drawingBounds.Bottom)));

    public static DrawingAttributes InkAttributes(uint color, double width, bool smooth, bool marker,
        InkTip tip = InkTip.Round) => new()
    {
        Color = ColorFrom(color), Width = width, Height = tip == InkTip.Chisel ? width / 2.5 : width,
        StylusTip = tip == InkTip.Round ? StylusTip.Ellipse : StylusTip.Rectangle,
        FitToCurve = smooth, IsHighlighter = marker
    };

    private static Stroke ToStroke(Ink ink)
    {
        var points = new StylusPointCollection(ink.Points.Select(p => new StylusPoint(p.X, p.Y, p.Pressure)));
        return new Stroke(points, InkAttributes(ink.Argb, ink.Width, ink.Smooth, ink.Highlighter, ink.Tip));
    }

    private static Color ColorFrom(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static Point ToPoint(BoardPoint point) => new(point.X, point.Y);

    private static DrawingGroup For(Content content) => Drawings.GetValue(content, MakeDrawing);

    internal static void DrawInk(DrawingContext dc, Ink ink) => dc.DrawDrawing(For(ink));

    internal static void DrawItems(DrawingContext dc, IEnumerable<Content> items)
    {
        foreach (var item in items)
            dc.DrawDrawing(For(item));
    }

    private static DrawingGroup MakeDrawing(Content item)
    {
        var drawing = new DrawingGroup();
        var brush = new SolidColorBrush(ColorFrom(item.Argb));
        using (var dc = drawing.Open())
        {
            switch (item)
            {
                case Ink ink:
                    var stroke = ToStroke(ink);
                    if (ink.Highlighter)
                    {
                        dc.PushOpacity(.45);
                        stroke.DrawingAttributes.IsHighlighter = false;
                        stroke.Draw(dc);
                        dc.Pop();
                    }
                    else
                        stroke.Draw(dc);
                    break;
                case Shape shape:
                    var pen = new Pen(brush, shape.Width)
                    {
                        StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
                    };
                    dc.DrawGeometry(null, pen, ShapeGeometry(shape));
                    break;
                case Equation equation:
                    MathParser.TryParse(equation.Source, out var expression, out _);
                    dc.PushTransform(new TranslateTransform(equation.Origin.X, equation.Origin.Y));
                    dc.DrawDrawing(new MathLayout(brush).Render(expression!, equation.Size));
                    dc.Pop();
                    break;
                case BoardText text:
                    dc.DrawText(Format(text.Text, text.Size, brush), ToPoint(text.Origin));
                    break;
                case Graph graph:
                    DrawGraph(dc, graph, brush);
                    break;
                case ChemicalEquation equation:
                    if (Chemistry.TryFormat(equation.Source, out var notation, out _, out _))
                    {
                        var formula = Format(notation, equation.Size, brush);
                        formula.MaxTextWidth = BoardWidth - 40;
                        dc.DrawText(formula, ToPoint(equation.Origin));
                    }
                    break;
                case TeachingDiagram diagram:
                    dc.PushTransform(new TranslateTransform(diagram.Origin.X, diagram.Origin.Y));
                    DiagramDrawing.Draw(dc, diagram, brush);
                    dc.Pop();
                    break;
            }
        }
        if (item.Placement is { } placement)
            drawing.Transform = new MatrixTransform(placement.Scale, 0, 0, placement.Scale, placement.X, placement.Y);
        drawing.Freeze();
        return drawing;
    }

    internal static Geometry ShapeGeometry(Shape shape)
    {
        var a = ToPoint(shape.Start);
        var b = ToPoint(shape.End);
        var radius = (b - a).Length;
        return shape.ShapeKind switch
        {
            ShapeKind.Line => new LineGeometry(a, b),
            ShapeKind.Arrow => ArrowGeometry(a, b, Math.Max(14, shape.Width * 3)),
            ShapeKind.Rectangle when shape.Corner is { } corner =>
                Polyline([a, b, b + (ToPoint(corner) - a), ToPoint(corner)], close: true),
            ShapeKind.Rectangle => new RectangleGeometry(new Rect(a, b)),
            ShapeKind.Ellipse => new EllipseGeometry(new Rect(a, b)),
            ShapeKind.Triangle => TriangleGeometry(a, b, shape.Corner),
            ShapeKind.Polygon when shape.Vertices is { Length: >= 3 } vertices =>
                Polyline(vertices.Select(ToPoint).ToArray(), close: true),
            ShapeKind.Cone or ShapeKind.Sphere or ShapeKind.Cube or ShapeKind.Cuboid or ShapeKind.Cylinder =>
                SolidGeometry.Draw(shape.ShapeKind, new Rect(a, b)),
            ShapeKind.Resistor or ShapeKind.Capacitor or ShapeKind.Equal => SymbolGeometry(shape.ShapeKind, a, b),
            ShapeKind.DoubleBond or ShapeKind.TripleBond => BondGeometry(shape, a, b),
            ShapeKind.Circle => new EllipseGeometry(a, radius, radius),
            ShapeKind.Arc when Math.Abs(shape.Sweep) >= 359.999 => new EllipseGeometry(a, radius, radius),
            ShapeKind.Arc => ArcGeometry(a, b, radius, shape.Sweep),
            _ => Geometry.Empty
        };
    }

    private static Geometry TriangleGeometry(Point a, Point b, BoardPoint? corner)
    {
        if (corner is { } point)
            return Polyline([a, b, ToPoint(point)], close: true);
        var bounds = new Rect(a, b);
        return Polyline([bounds.BottomLeft, bounds.BottomRight, new Point(bounds.Left + bounds.Width / 2, bounds.Top)], close: true);
    }

    private static Geometry SymbolGeometry(ShapeKind kind, Point a, Point b)
    {
        var delta = b - a;
        var normal = new Vector(-delta.Y, delta.X);
        Point At(double along, double across = 0) => a + delta * along + normal * across;
        return kind switch
        {
            ShapeKind.Resistor => Polyline([a, At(.18), At(.24, -.11), At(.34, .11), At(.44, -.11),
                At(.54, .11), At(.64, -.11), At(.74, .11), At(.80), b]),
            ShapeKind.Capacitor => new GeometryGroup
            {
                Children = { new LineGeometry(a, At(.44)), new LineGeometry(At(.56), b),
                    new LineGeometry(At(.44, -.19), At(.44, .19)), new LineGeometry(At(.56, -.19), At(.56, .19)) }
            },
            _ => new GeometryGroup
            {
                Children = { new LineGeometry(At(0, -.15), At(1, -.15)), new LineGeometry(At(0, .15), At(1, .15)) }
            }
        };
    }

    internal static Geometry Polyline(IReadOnlyList<Point> points, bool close = false, bool fill = false)
    {
        var line = new StreamGeometry();
        using var geometry = line.Open();
        geometry.BeginFigure(points[0], fill, close);
        geometry.PolyLineTo(points.Skip(1).ToArray(), true, false);
        return line;
    }

    private static Geometry BondGeometry(Shape shape, Point start, Point end)
    {
        var axis = end - start;
        if (axis.LengthSquared == 0)
            return Geometry.Empty;
        var normal = new Vector(-axis.Y, axis.X);
        normal.Normalize();
        var count = shape.ShapeKind == ShapeKind.TripleBond ? 3 : 2;
        var bonds = new GeometryGroup();
        for (var i = 0; i < count; i++)
        {
            var offset = normal * ((i - (count - 1) / 2d) * Math.Max(8, shape.Width * 1.7));
            bonds.Children.Add(new LineGeometry(start + offset, end + offset));
        }
        return bonds;
    }

    internal static Geometry ArrowGeometry(Point start, Point end, double headLength)
    {
        var delta = end - start;
        var length = delta.Length;
        if (length < .01)
            return Geometry.Empty;
        var back = delta * (Math.Min(length * .65, headLength) / length);
        var side = new Vector(-back.Y, back.X) * .55;
        var arrow = new StreamGeometry();
        using (var geometry = arrow.Open())
        {
            geometry.BeginFigure(start, false, false);
            geometry.LineTo(end, true, false);
            geometry.BeginFigure(end - back + side, false, false);
            geometry.LineTo(end, true, false);
            geometry.LineTo(end - back - side, true, false);
        }
        return arrow;
    }

    internal static void DrawPointer(DrawingContext dc, Point point)
    {
        var arrow = ArrowGeometry(point + new Vector(33, 38), point, 20);
        dc.DrawGeometry(null, new Pen(Brushes.Black, 9), arrow);
        dc.DrawGeometry(null, new Pen(Brushes.Gold, 5), arrow);
    }

    public void MarkPointer(Point? point)
    {
        using var dc = overlay.RenderOpen();
        if (point is { } position)
            DrawPointer(dc, position);
    }

    private static Geometry ArcGeometry(Point center, Point start, double radius, double sweep)
    {
        var rotation = sweep * Math.PI / 180;
        var angle = Math.Atan2(start.Y - center.Y, start.X - center.X) + rotation;
        var end = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        var arc = new StreamGeometry();
        using (var geo = arc.Open())
        {
            geo.BeginFigure(start, false, false);
            geo.ArcTo(end, new Size(radius, radius), 0, Math.Abs(sweep) > 180,
                sweep >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        }
        return arc;
    }

    internal static uint PaperColor(BoardPaper paper, uint? color) =>
        color ?? (paper == BoardPaper.Dark ? 0xff1a2532u : 0xfffafcfau);

    internal static bool DarkPaper(BoardPaper paper, uint? color)
    {
        var value = PaperColor(paper, color);
        return .2126 * ((value >> 16) & 255) + .7152 * ((value >> 8) & 255) + .0722 * (value & 255) < 130;
    }

    private static DrawingGroup MakePaper(BoardPaper paper, uint? color)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(new SolidColorBrush(ColorFrom(PaperColor(paper, color))), null, Bounds);
            var dark = DarkPaper(paper, color);
            var rule = new SolidColorBrush(dark ? Color.FromArgb(55, 226, 240, 245) : Color.FromArgb(45, 45, 83, 104));
            var pen = new Pen(rule, 1);
            var major = new Pen(rule, 2.5);
            var step = paper == BoardPaper.Graph ? 16 : 32;
            for (var y = step; y < BoardHeight; y += step)
            {
                if (paper is BoardPaper.Grid or BoardPaper.Ruled or BoardPaper.Graph)
                    dc.DrawLine(paper == BoardPaper.Graph && y % 80 == 0 ? major : pen, new Point(0, y), new Point(BoardWidth, y));
                if (paper == BoardPaper.Dots)
                    for (var x = 32; x < BoardWidth; x += 32)
                        dc.DrawEllipse(rule, null, new Point(x, y), 1.5, 1.5);
            }
            if (paper is BoardPaper.Grid or BoardPaper.Graph)
                for (var x = step; x < BoardWidth; x += step)
                    dc.DrawLine(paper == BoardPaper.Graph && x % 80 == 0 ? major : pen, new Point(x, 0), new Point(x, BoardHeight));
            if (paper == BoardPaper.Isometric)
            {
                const double row = 32 * .8660254;
                var run = BoardHeight / Math.Sqrt(3);
                for (var y = row; y < BoardHeight; y += row)
                    dc.DrawLine(pen, new Point(0, y), new Point(BoardWidth, y));
                for (var x = -Math.Ceiling(run / 32) * 32; x < BoardWidth + run; x += 32)
                {
                    dc.DrawLine(pen, new Point(x, 0), new Point(x + run, BoardHeight));
                    dc.DrawLine(pen, new Point(x, 0), new Point(x - run, BoardHeight));
                }
            }
            if (paper == BoardPaper.Staff)
                for (var y = 50; y < BoardHeight - 60; y += 116)
                    for (var i = 0; i < 5; i++)
                        dc.DrawLine(pen, new Point(28, y + i * 12), new Point(BoardWidth - 28, y + i * 12));
        }
        drawing.Freeze();
        return drawing;
    }

    internal static FormattedText Format(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MathLayout.TextFace, size, brush, 1);

    private static void DrawGraph(DrawingContext dc, Graph graph, Brush brush)
    {
        const int half = 150;
        const int scale = 15;
        var origin = ToPoint(graph.Origin);
        var bounds = new Rect(origin.X, origin.Y, 320, 352);
        var center = new Point(origin.X + 160, origin.Y + 160);
        var clip = new Rect(center.X - half, center.Y - half, half * 2, half * 2);
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(217, 228, 230)), 1);
        var axis = new Pen(new SolidColorBrush(Color.FromRgb(114, 138, 150)), 1.8);
        dc.DrawRoundedRectangle(Brushes.White, grid, bounds, 6, 6);
        for (var i = -10; i <= 10; i++)
        {
            var pen = i == 0 ? axis : grid;
            dc.DrawLine(pen, new Point(center.X + i * scale, clip.Top), new Point(center.X + i * scale, clip.Bottom));
            dc.DrawLine(pen, new Point(clip.Left, center.Y + i * scale), new Point(clip.Right, center.Y + i * scale));
        }
        dc.DrawText(Format("−10", 10, Brushes.SlateGray), new Point(clip.Left + 2, center.Y + 2));
        dc.DrawText(Format("10", 10, Brushes.SlateGray), new Point(clip.Right - 16, center.Y + 2));
        dc.DrawText(Format("10", 10, Brushes.SlateGray), new Point(center.X + 3, clip.Top));
        dc.DrawText(Format("−10", 10, Brushes.SlateGray), new Point(center.X + 3, clip.Bottom - 14));
        var caption = Format(graph.Source, 15, brush);
        caption.MaxTextWidth = 299;
        caption.MaxLineCount = 1;
        caption.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(caption, new Point(clip.Left, clip.Bottom + 6));

        MathParser.TryParseGraph(graph.Source, out var expression, out _);
        var curve = new StreamGeometry();
        using (var geo = curve.Open())
        {
            var drawing = false;
            var previous = 0d;
            for (var i = -half * 2; i <= half * 2; i++)
            {
                var value = MathParser.Evaluate(expression!, i / (2d * scale));
                if (!double.IsFinite(value) || Math.Abs(value) > 12)
                {
                    drawing = false;
                    continue;
                }
                var next = new Point(center.X + i / 2d, center.Y - value * scale);
                if (drawing && Math.Abs(previous - value) < 4)
                    geo.LineTo(next, true, false);
                else
                    geo.BeginFigure(next, false, false);
                drawing = true;
                previous = value;
            }
        }
        dc.PushClip(new RectangleGeometry(clip));
        dc.DrawGeometry(null, new Pen(brush, 2.5), curve);
        dc.Pop();
    }
}
