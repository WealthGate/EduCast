using System.Globalization;
using System.Windows;
using System.Windows.Media;
using EduCast.Classroom;
using Matrix = System.Windows.Media.Matrix;

namespace EduCast.Studio;

internal enum InstrumentHandle { Move, Rotate, Resize, Angle, Curve, CompassTip, GaugeAdjust, GaugeAlign }
internal sealed record InstrumentDisplay(InstrumentPose Pose, string Expression = "", string Result = "",
    MeasurementGauge? Gauge = null);

internal sealed class TeacherInstrumentSurface : FrameworkElement
{
    private InstrumentDisplay[] instruments = [];
    private InstrumentDisplay? active;
    private Content? trace;
    private bool tracing;
    private (ClockReading Reading, Vector Offset)? clock;

    public TeacherInstrumentSurface() => IsHitTestVisible = false;

    public void Present(InstrumentDisplay[] next, InstrumentDisplay? selected, Content? pending,
        bool traceMode, (ClockReading Reading, Vector Offset)? nextClock)
    {
        if (next.SequenceEqual(instruments) && ReferenceEquals(selected, active) && Equals(pending, trace) &&
            traceMode == tracing && nextClock == clock)
            return;
        (instruments, active, trace, tracing, clock) = (next, selected, pending, traceMode, nextClock);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var display in instruments)
        {
            if (!ReferenceEquals(display, active))
                InstrumentDrawing.Draw(dc, display);
        }
        if (active is not null)
            InstrumentDrawing.Draw(dc, active, controls: true, tracing);
        if (trace is not null)
            BoardSurface.DrawItems(dc, [trace]);
        if (clock is { } visible)
            ProgramSurface.DrawClock(dc, visible.Reading, visible.Offset);
    }
}

internal static class InstrumentDrawing
{
    private static readonly Brush Fill = ColorBrush(145, 174, 226, 232);
    private static readonly Brush Ink = ColorBrush(255, 17, 46, 60);
    private static readonly Brush Accent = ColorBrush(255, 23, 119, 115);
    private static readonly Brush Pencil = ColorBrush(255, 208, 134, 43);
    private static readonly Brush Calculator = ColorBrush(250, 20, 36, 51);
    private static readonly Brush Readout = ColorBrush(255, 212, 248, 231);
    private static readonly Typeface Font = new("Segoe UI Semibold");
    private static readonly Pen Border = MakePen(Ink, 2);
    private static readonly Pen Tick = MakePen(Ink, 1);
    private static readonly Pen Guide = MakePen(Accent, 2);
    private static readonly Pen Dashed = MakePen(Accent, 1.5, DashStyles.Dash);

    public static (InstrumentHandle Handle, BoardPoint Position)[] Handles(InstrumentPose pose)
    {
        var move = pose.Kind switch
        {
            InstrumentKind.Ruler => new BoardPoint(pose.Size / 2, pose.RulerHeight + 24),
            InstrumentKind.Compass => new BoardPoint(pose.Size / 2, 28),
            InstrumentKind.Protractor360 => new BoardPoint(0, pose.Size + 60),
            InstrumentKind.FrenchCurve => new BoardPoint(0, Math.Max(0, -pose.CurveRise) + 53),
            InstrumentKind.Calculator => new BoardPoint(24, -23),
            _ => new BoardPoint(0, 26)
        };
        var rotate = pose.Kind switch
        {
            InstrumentKind.Ruler => new BoardPoint(-32, pose.RulerHeight / 2),
            InstrumentKind.Compass => pose.Hinge with { Y = pose.Hinge.Y - 26 },
            InstrumentKind.Protractor180 => new BoardPoint(-pose.Size - 25, 25),
            InstrumentKind.Protractor360 => new BoardPoint(-pose.Size - 60, 0),
            InstrumentKind.FrenchCurve => new BoardPoint(0, Math.Min(0, -pose.CurveRise) - 25),
            _ => new BoardPoint(-34, -22)
        };
        var resize = pose.Kind switch
        {
            InstrumentKind.Ruler => new BoardPoint(pose.Size + 25, pose.RulerHeight / 2),
            InstrumentKind.Compass => new BoardPoint(pose.Size + 25, 26),
            InstrumentKind.FrenchCurve => pose.CurveAt(pose.Size / 2) with { X = pose.Size / 2 + 26 },
            InstrumentKind.Calculator => new BoardPoint(pose.Size + 25, pose.Size * .43),
            InstrumentKind.Protractor360 => new BoardPoint(pose.Size + 60, 0),
            _ => new BoardPoint(pose.Size + 25, 25)
        };
        var controls = new List<(InstrumentHandle, BoardPoint)> { (InstrumentHandle.Move, move), (InstrumentHandle.Resize, resize) };
        if (pose.Kind != InstrumentKind.Calculator)
            controls.Add((InstrumentHandle.Rotate, rotate));
        if (pose.IsProtractor)
            controls.Add((InstrumentHandle.Angle, InstrumentPose.Polar(-pose.Angle, pose.Size + 26)));
        if (pose.Kind == InstrumentKind.FrenchCurve)
            controls.Add((InstrumentHandle.Curve, pose.CurveAt(-pose.Size / 2) with { X = -pose.Size / 2 - 26 }));
        return controls.ToArray();
    }

    public static bool Contains(InstrumentPose pose, BoardPoint local) => pose.Kind switch
    {
        InstrumentKind.Ruler => local.X >= 0 && local.X <= pose.Size && local.Y >= 0 && local.Y <= pose.RulerHeight,
        InstrumentKind.Compass => InstrumentPose.Distance(local, new GuideEdge(new(0, 0), pose.Hinge).Closest(local)) < 22 ||
            InstrumentPose.Distance(local, new GuideEdge(pose.Hinge, new(pose.Size, 0)).Closest(local)) < 22,
        InstrumentKind.Protractor180 or InstrumentKind.Protractor360 =>
            (pose.Kind == InstrumentKind.Protractor360 || local.Y <= 8) &&
            InstrumentPose.Distance(default, local) <= pose.Size,
        InstrumentKind.SetSquare45 or InstrumentKind.SetSquare30 => local.X >= 0 && local.Y <= 0 &&
            local.X / pose.Size - local.Y / pose.TriangleHeight <= 1,
        InstrumentKind.FrenchCurve => InstrumentPose.Distance(local, pose.CurveAt(local.X)) < 24,
        _ => local.X >= 0 && local.X <= pose.Size && local.Y >= 0 && local.Y <= pose.Size * .43
    };

    public static void Draw(DrawingContext dc, InstrumentDisplay display, bool controls = false, bool tracing = false)
    {
        if (display.Gauge is { } gauge)
        {
            GaugeDrawing.Draw(dc, display, gauge, controls);
            return;
        }
        var pose = display.Pose;
        var transform = Matrix.Identity;
        transform.Rotate(pose.Rotation);
        transform.Translate(pose.Origin.X, pose.Origin.Y);
        dc.PushTransform(new MatrixTransform(transform));
        switch (pose.Kind)
        {
            case InstrumentKind.Ruler: Ruler(dc, pose); break;
            case InstrumentKind.Compass: Compass(dc, pose, controls && tracing); break;
            case InstrumentKind.Protractor180:
            case InstrumentKind.Protractor360: Protractor(dc, pose); break;
            case InstrumentKind.SetSquare45:
            case InstrumentKind.SetSquare30: SetSquare(dc, pose); break;
            case InstrumentKind.FrenchCurve: FrenchCurve(dc, pose); break;
            case InstrumentKind.Calculator: CalculatorFace(dc, display); break;
        }

        if (controls)
        {
            foreach (var (handle, position) in Handles(pose))
            {
                var symbol = handle switch
                {
                    InstrumentHandle.Move => "+", InstrumentHandle.Rotate => "↻",
                    InstrumentHandle.Resize => "↔", InstrumentHandle.Angle => "∠", _ => "±"
                };
                dc.DrawEllipse(handle == InstrumentHandle.Rotate ? Pencil : Readout, Border, PointOf(position), 12, 12);
                Center(dc, symbol, PointOf(position), 15, Ink);
            }
        }
        dc.Pop();
    }

    private static void Ruler(DrawingContext dc, InstrumentPose pose)
    {
        dc.DrawRoundedRectangle(Fill, Border, new Rect(0, 0, pose.Size, pose.RulerHeight), 4, 4);
        var unit = pose.UnitsPerMeasure;
        var divisions = unit >= 24 ? (pose.Inches ? 8 : 10) : unit >= 10 ? (pose.Inches ? 2 : 5) : 1;
        var interval = unit / divisions;
        for (var index = 0; index * interval <= pose.Size; index++)
        {
            var x = index * interval;
            var major = index % divisions == 0;
            var height = major ? 22 : index % Math.Max(1, divisions / 2) == 0 ? 13 : 8;
            dc.DrawLine(Tick, new Point(x, 0), new Point(x, height));
            if (major && x < pose.Size - 14 && (unit >= 20 || index / divisions % 2 == 0))
                dc.DrawText(Text((index / divisions).ToString(CultureInfo.InvariantCulture), 13, Ink), new Point(x + 3, 23));
        }
        dc.DrawText(Text(pose.Unit, 13, Ink), new Point(pose.Size - 27, pose.RulerHeight - 18));
        dc.DrawGeometry(Accent, null, BoardSurface.Polyline([new(-5, -11), new(5, -11), new(0, -2)], close: true, fill: true));
    }

    private static void Compass(DrawingContext dc, InstrumentPose pose, bool traceGuide)
    {
        var hinge = PointOf(pose.Hinge);
        var pencil = new Point(pose.Size, 0);
        var pivot = new Point();
        if (traceGuide)
            dc.DrawEllipse(null, Dashed, pivot, pose.Size, pose.Size);
        Leg(pivot, Accent, Fill);
        Leg(pencil, Pencil, Pencil);
        dc.DrawEllipse(Readout, Border, hinge, 15, 15);

        void Leg(Point tip, Brush pointBrush, Brush armBrush)
        {
            var axis = hinge - tip;
            axis.Normalize();
            var shoulder = tip + axis * 19;
            var side = new Vector(-axis.Y, axis.X);
            dc.DrawLine(MakePen(Ink, 15), shoulder, hinge);
            dc.DrawLine(MakePen(armBrush, 9), shoulder, hinge);
            dc.DrawGeometry(pointBrush, null, BoardSurface.Polyline(
                [tip, shoulder + side * 4, shoulder - side * 4], close: true, fill: true));
        }
    }

    private static void Protractor(DrawingContext dc, InstrumentPose pose)
    {
        var radius = pose.Size;
        var center = new Point();
        var full = pose.Kind == InstrumentKind.Protractor360;
        var max = full ? 360 : 180;
        if (full)
            dc.DrawEllipse(Fill, Border, center, radius, radius);
        else
        {
            var shell = new StreamGeometry();
            using (var path = shell.Open())
            {
                path.BeginFigure(new Point(-radius, 0), true, true);
                path.ArcTo(new Point(radius, 0), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
            dc.DrawGeometry(Fill, Border, shell);
        }
        dc.DrawLine(Guide, new Point(5, 0), new Point(radius - 46, 0));
        dc.DrawLine(MakePen(Pencil, 2), PointOf(InstrumentPose.Polar(-pose.Angle, 5)),
            PointOf(InstrumentPose.Polar(-pose.Angle, radius - 46)));
        dc.DrawLine(MakePen(Pencil, 2), PointOf(InstrumentPose.Polar(-pose.Angle, radius + 3)),
            PointOf(InstrumentPose.Polar(-pose.Angle, radius + 10)));
        dc.DrawEllipse(null, Tick, center, 4, 4);
        for (var degree = 0; degree <= max - (full ? 1 : 0); degree++)
        {
            var outside = PointOf(InstrumentPose.Polar(-degree, radius));
            var inside = PointOf(InstrumentPose.Polar(-degree, radius - (degree % 10 == 0 ? 15 : degree % 5 == 0 ? 10 : 5)));
            dc.DrawLine(Tick, outside, inside);
            if (degree % 30 == 0)
                Center(dc, degree.ToString(CultureInfo.InvariantCulture),
                    PointOf(InstrumentPose.Polar(-degree, radius - 29)), 12, Ink);
        }
        var reading = new Point(0, full ? radius + 89 : 54);
        dc.DrawRoundedRectangle(Readout, null, new Rect(reading.X - 27, reading.Y - 12, 54, 24), 4, 4);
        Center(dc, $"{pose.Angle:F0}°", reading, 17, Ink);
    }

    private static void SetSquare(DrawingContext dc, InstrumentPose pose)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(), true, true);
            path.PolyLineTo([new Point(pose.Size, 0), new Point(0, -pose.TriangleHeight)], true, false);
        }
        dc.DrawGeometry(Fill, Border, geometry);
        dc.DrawLine(Tick, new Point(17, 0), new Point(17, -17));
        dc.DrawLine(Tick, new Point(17, -17), new Point(0, -17));
        Center(dc, pose.Kind == InstrumentKind.SetSquare45 ? "45° / 45°" : "30° / 60°",
            new Point(pose.Size * .29, -pose.TriangleHeight * .28), 15, Ink);
    }

    private static void FrenchCurve(DrawingContext dc, InstrumentPose pose)
    {
        var left = PointOf(pose.CurveAt(-pose.Size / 2));
        var right = PointOf(pose.CurveAt(pose.Size / 2));
        var band = new StreamGeometry();
        using (var path = band.Open())
        {
            path.BeginFigure(left, true, true);
            path.QuadraticBezierTo(new Point(0, pose.CurveRise), right, true, false);
            path.LineTo(right + new Vector(0, 28), true, false);
            path.QuadraticBezierTo(new Point(0, pose.CurveRise + 28), left + new Vector(0, 28), true, false);
        }
        dc.DrawGeometry(Fill, Border, band);
        Center(dc, "QUADRATIC GUIDE", new Point(0, 13), 13, Ink);
    }

    private static void CalculatorFace(DrawingContext dc, InstrumentDisplay display)
    {
        var width = display.Pose.Size;
        var height = width * .43;
        dc.DrawRoundedRectangle(Calculator, Guide, new Rect(0, 0, width, height), 13, 13);
        dc.DrawText(Text("SCIENTIFIC CALCULATOR", Math.Clamp(width / 21, 11, 18), Readout), new Point(33, 13));
        var padding = width * .06;
        dc.DrawRoundedRectangle(Readout, null, new Rect(padding, height * .34, width - padding * 2, height * .52), 7, 7);
        var expression = Text(display.Expression.Length > 0 ? display.Expression : "Enter a calculation", width / 20, Ink);
        expression.MaxTextWidth = width - padding * 3;
        expression.Trimming = TextTrimming.CharacterEllipsis;
        expression.MaxLineCount = 1;
        dc.DrawText(expression, new Point(padding * 1.5, height * .39));
        var result = Text(display.Result.Length > 0 ? display.Result : "—", width / 13, Ink);
        result.MaxTextWidth = width - padding * 3;
        result.Trimming = TextTrimming.CharacterEllipsis;
        result.MaxLineCount = 1;
        dc.DrawText(result, new Point(padding * 1.5, height * .57));
    }

    private static FormattedText Text(string value, double size, Brush brush) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush, 1);

    private static void Center(DrawingContext dc, string value, Point center, double size, Brush brush)
    {
        var text = Text(value, size, brush);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static Point PointOf(BoardPoint point) => new(point.X, point.Y);

    private static Brush ColorBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(Brush brush, double width, DashStyle? dash = null)
    {
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (dash is not null)
            pen.DashStyle = dash;
        pen.Freeze();
        return pen;
    }
}
