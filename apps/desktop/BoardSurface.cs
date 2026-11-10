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
    private static readonly Dictionary<BoardPaper, DrawingGroup> Papers = [];
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
        Draw(dc, frame.Paper, frame.Items);
    }

    public void Edit(Page page)
    {
        using var dc = board.RenderOpen();
        Draw(dc, page.Paper, page.Items.Where(item => item.Visibility is ItemVisibility.Both or ItemVisibility.TeacherOnly));
    }

    private static void Draw(DrawingContext dc, BoardPaper paper, IEnumerable<Content> items)
    {
        dc.PushClip(new RectangleGeometry(Bounds));
        if (!Papers.TryGetValue(paper, out var background))
            Papers[paper] = background = MakePaper(paper);
        dc.DrawDrawing(background);
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
            Shape shape => shape with { Start = Shift(shape.Start), End = Shift(shape.End) },
            Equation equation => equation with { Origin = Shift(equation.Origin) },
            BoardText text => text with { Origin = Shift(text.Origin) },
            Graph graph => graph with { Origin = Shift(graph.Origin) },
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
            }
        }
        if (item.Placement is { } placement)
            drawing.Transform = new MatrixTransform(placement.Scale, 0, 0, placement.Scale, placement.X, placement.Y);
        drawing.Freeze();
        return drawing;
    }

    private static Geometry ShapeGeometry(Shape shape)
    {
        var a = ToPoint(shape.Start);
        var b = ToPoint(shape.End);
        var radius = (b - a).Length;
        return shape.ShapeKind switch
        {
            ShapeKind.Line => new LineGeometry(a, b),
            ShapeKind.Arrow => ArrowGeometry(a, b, Math.Max(14, shape.Width * 3)),
            ShapeKind.Rectangle => new RectangleGeometry(new Rect(a, b)),
            ShapeKind.Circle => new EllipseGeometry(a, radius, radius),
            ShapeKind.Arc when Math.Abs(shape.Sweep) >= 359.999 => new EllipseGeometry(a, radius, radius),
            ShapeKind.Arc => ArcGeometry(a, b, radius, shape.Sweep),
            _ => Geometry.Empty
        };
    }

    private static Geometry ArrowGeometry(Point start, Point end, double headLength)
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

    private static DrawingGroup MakePaper(BoardPaper paper)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var bg = paper == BoardPaper.Dark ? Color.FromRgb(26, 37, 50) : Color.FromRgb(250, 252, 250);
            dc.DrawRectangle(new SolidColorBrush(bg), null, Bounds);
            var rule = new SolidColorBrush(Color.FromRgb(211, 224, 226));
            var pen = new Pen(rule, 1);
            for (var y = 32; y < BoardHeight; y += 32)
            {
                if (paper is BoardPaper.Grid or BoardPaper.Ruled)
                    dc.DrawLine(pen, new Point(0, y), new Point(BoardWidth, y));
                if (paper == BoardPaper.Dots)
                    for (var x = 32; x < BoardWidth; x += 32)
                        dc.DrawEllipse(rule, null, new Point(x, y), 1.5, 1.5);
            }
            if (paper == BoardPaper.Grid)
                for (var x = 32; x < BoardWidth; x += 32)
                    dc.DrawLine(pen, new Point(x, 0), new Point(x, BoardHeight));
        }
        drawing.Freeze();
        return drawing;
    }

    private static FormattedText Format(string text, double size, Brush brush) =>
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
