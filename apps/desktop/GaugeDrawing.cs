using System.Globalization;
using System.Windows;
using System.Windows.Media;
using EduCast.Classroom;

namespace EduCast.Studio;

internal static class GaugeDrawing
{
    private static readonly Brush Steel = Gradient(("#F4FAFF", 0), ("#9AB1C0", .23), ("#DCEAF1", .48), ("#648092", 1));
    private static readonly Brush DarkSteel = Gradient(("#B6CAD5", 0), ("#516B7D", .35), ("#849CA9", .6), ("#294352", 1));
    private static readonly Brush Teal = Gradient(("#52A9A1", 0), ("#287F79", .44), ("#103D49", 1));
    private static readonly Brush Rim = Paint("#183747");
    private static readonly Brush Shadow = Paint("#102630");
    private static readonly Brush Dial = Paint("#F0F7FA");
    private static readonly Brush Scale = Paint("#163649");
    private static readonly Brush Highlight = Paint("#A73617");
    private static readonly Brush Sample = Paint("#F2B66E");
    private static readonly Brush Panel = Paint("#EAF5F8");
    private static readonly Pen Outline = Stroke(Rim, 1.6);
    private static readonly Pen Tick = Stroke(Scale, 1);
    private static readonly Pen Major = Stroke(Scale, 1.45);
    private static readonly Pen Match = Stroke(Highlight, 2.2);
    private static readonly Pen DatumPen = Stroke(Paint("#11746F"), 2);

    private const double RailY = -111;
    private const double Seam = -81;
    private const double InsideY = -132;
    private const double DepthY = -70;
    private const double SleeveZero = 100;
    private const double ThimbleLength = 128;

    internal static double Unit(InstrumentPose pose) => pose.UnitsPerCentimetre / 10;
    private static double Gap(InstrumentPose pose, MeasurementGauge gauge) => gauge.Opening * Unit(pose) / 100;
    private static double BeamEnd(InstrumentPose pose) => 203 * Unit(pose);

    internal static BoardPoint Datum(InstrumentPose pose, GaugeMode mode) => mode switch
    {
        GaugeMode.Inside => new BoardPoint(0, InsideY),
        GaugeMode.Depth => new BoardPoint(BeamEnd(pose), DepthY),
        _ => new BoardPoint(0, 0)
    };

    private static Rect Bounds(InstrumentPose pose, MeasurementGauge gauge)
    {
        if (gauge.IsCaliper)
        {
            var extension = gauge.Mode == GaugeMode.Depth
                ? Math.Max(Gap(pose, gauge), gauge.SpecimenSize * Unit(pose) / 100 + 16)
                : 0;
            var rear = BeamEnd(pose) + extension + 8;
            return new Rect(-34, gauge.Mode == GaugeMode.Inside ? -177 : -143, rear + 34, gauge.Mode == GaugeMode.Inside ? 214 : 180);
        }
        return new Rect(-86, -41, SleeveZero + Gap(pose, gauge) + ThimbleLength + 38 + 86, 158);
    }

    private static Rect Adjuster(InstrumentPose pose, MeasurementGauge gauge)
    {
        var gap = Gap(pose, gauge);
        return gauge.IsCaliper
            ? new Rect(gap, Seam + 2, Math.Max(31, 49 * Unit(pose) + 12), 42)
            : new Rect(SleeveZero + gap, -39, ThimbleLength + 36, 78);
    }

    internal static bool OverAdjuster(InstrumentPose pose, MeasurementGauge gauge, BoardPoint local)
    {
        var datum = Datum(pose, gauge.Mode);
        var p = new Point(local.X + datum.X, local.Y + datum.Y);
        var gap = Gap(pose, gauge);
        return Adjuster(pose, gauge).Contains(p) || gauge.IsCaliper &&
            (new Rect(gap + 1, -51, 25, 80).Contains(p) || new Rect(gap + 2, InsideY - 4, 23, 27).Contains(p));
    }

    internal static InstrumentHandle? Hit(InstrumentPose pose, MeasurementGauge gauge, BoardPoint local)
    {
        var datum = Datum(pose, gauge.Mode);
        var p = new Point(local.X + datum.X, local.Y + datum.Y);
        var bounds = Bounds(pose, gauge);
        if ((p - RotateHandle(gauge, bounds)).Length <= 16)
            return InstrumentHandle.Rotate;
        if (OverAdjuster(pose, gauge, local))
            return InstrumentHandle.GaugeAdjust;
        return bounds.Contains(p) ? InstrumentHandle.Move : null;
    }

    private static Point RotateHandle(MeasurementGauge gauge, Rect bounds) =>
        new(bounds.Left - 23, gauge.IsCaliper ? -86 : 35);

    internal static void Draw(DrawingContext dc, InstrumentDisplay display, MeasurementGauge gauge, bool controls)
    {
        var pose = display.Pose;
        var datum = Datum(pose, gauge.Mode);
        var transform = System.Windows.Media.Matrix.Identity;
        transform.Translate(-datum.X, -datum.Y);
        transform.Rotate(pose.Rotation);
        transform.Translate(pose.Origin.X, pose.Origin.Y);
        dc.PushTransform(new MatrixTransform(transform));
        if (gauge.IsCaliper)
            Caliper(dc, pose, gauge);
        else
            Micrometer(dc, pose, gauge);
        var bounds = Bounds(pose, gauge);
        if (controls)
        {
            var handle = RotateHandle(gauge, bounds);
            dc.DrawEllipse(Dial, Outline, handle, 12, 12);
            Label(dc, "↻", handle.X, handle.Y - 10, 15);
        }
        dc.Pop();

        if (!gauge.CloseUp && !gauge.ShowReading)
            return;
        var body = Rect.Transform(bounds, transform);
        var width = gauge.CloseUp ? 854 : 360;
        var height = gauge.CloseUp ? 166 : 56;
        var top = new Rect((1280 - width) / 2, 20, width, height);
        var bottom = new Rect(top.X, 700 - height, width, height);
        var card = Overlap(top, body) < Overlap(bottom, body) ? top : bottom;
        dc.PushTransform(new TranslateTransform(card.X, card.Y));
        dc.DrawRoundedRectangle(Panel, Outline, new Rect(0, 0, width, height), 12, 12);
        if (gauge.CloseUp)
        {
            if (gauge.IsCaliper)
                VernierCloseUp(dc, gauge);
            else
                MicrometerCloseUp(dc, gauge);
        }
        else
        {
            Label(dc, "MEASUREMENT  /  MILLIMETRES", width / 2, 6, 12);
            Label(dc, Reading(gauge), width / 2, 21, 23, Highlight);
        }
        dc.Pop();
    }

    internal static string Millimetres(int hundredths) =>
        (hundredths / 100m).ToString("0.00", CultureInfo.CurrentCulture);

    internal static string Reading(MeasurementGauge gauge) =>
        $"{Millimetres(gauge.MainReading)} + {Millimetres(gauge.FineReading)} = {Millimetres(gauge.Opening)} mm";

    private static double Overlap(Rect card, Rect body)
    {
        var overlap = Rect.Intersect(card, body);
        return overlap.IsEmpty ? 0 : overlap.Width * overlap.Height;
    }

    private static void Caliper(DrawingContext dc, InstrumentPose pose, MeasurementGauge gauge)
    {
        var unit = Unit(pose);
        var gap = Gap(pose, gauge);
        var end = BeamEnd(pose);
        var slider = Math.Max(30, unit * 49 + 8);
        // The back edge and the thickness of the two jaws sit behind their true measuring faces.
        Box(dc, new Rect(-16, RailY + 5, end + 16, 37), DarkSteel, 4);
        Box(dc, new Rect(-16, RailY, end + 16, 30), Steel, 4);
        Box(dc, new Rect(gap, Seam, slider, 31), Steel, 4);
        Face(dc, [new(-28, -98), new(0, -98), new(0, 28), new(-9, 35), new(-28, -13)], Steel);
        Face(dc, [new(gap, -64), new(gap + 26, -64), new(gap + 26, -12), new(gap + 10, 34), new(gap, 29)], Steel);
        Face(dc, [new(0, InsideY), new(10, -128), new(16, -104), new(0, -104)], Steel);
        Face(dc, [new(gap, InsideY), new(gap, -104), new(gap - 15, -104), new(gap - 10, -128)], Steel);

        if (gauge.Mode == GaugeMode.Depth)
            Box(dc, new Rect(end - 3, DepthY - 4, Math.Max(3, gap + 3), 8), DarkSteel, 1);
        DrawSample(dc, pose, gauge);
        DrawCaliperContact(dc, gauge, gap, end);

        for (var mm = 0; mm <= 199; mm++)
        {
            var x = mm * unit;
            Line(dc, x, Seam, x, Seam - (mm % 10 == 0 ? 15 : mm % 5 == 0 ? 10 : 5), Tick);
            if (mm % (unit < 2.5 ? 20 : 10) == 0)
                Label(dc, mm.ToString(CultureInfo.InvariantCulture), x, RailY + 1, 10);
        }
        for (var i = 0; i <= MeasurementGauge.VernierDivisions; i++)
        {
            var x = gauge.VernierTick(i) * unit;
            Line(dc, x, Seam + 1, x, Seam + (i % 10 == 0 ? 14 : i % 5 == 0 ? 9 : 5), i == 0 ? Major : Tick);
            if (i % (unit < 2.5 ? 20 : 10) == 0)
                Label(dc, i.ToString(CultureInfo.InvariantCulture), x, Seam + 15, 9);
        }
        var grip = new Rect(gap + Math.Max(5, slider * .5 - 16), -51, 31, 14);
        Box(dc, grip, DarkSteel, 5);
        for (var i = 6; i < 28; i += 5)
            Line(dc, grip.X + i, grip.Y + 3, grip.X + i, grip.Bottom - 3, Tick);
    }

    private static void DrawCaliperContact(DrawingContext dc, MeasurementGauge gauge, double gap, double end)
    {
        var edge = gauge.AtContact ? Match : DatumPen;
        if (gauge.Mode == GaugeMode.Depth)
        {
            Line(dc, end, DepthY - 8, end, DepthY + 8, DatumPen);
            Line(dc, end + gap, DepthY - 5, end + gap, DepthY + 5, edge);
        }
        else
        {
            var y = gauge.Mode == GaugeMode.Inside ? InsideY : 0;
            Line(dc, 0, y - 7, 0, y + 7, DatumPen);
            Line(dc, gap, y - 7, gap, y + 7, edge);
        }
    }

    private static void DrawSample(DrawingContext dc, InstrumentPose pose, MeasurementGauge gauge)
    {
        if (gauge.Sample == GaugeSample.None)
            return;
        var size = gauge.SpecimenSize * Unit(pose) / 100;
        if (gauge.Mode == GaugeMode.Inside)
        {
            Face(dc, [new(-17, InsideY + 10), new(-17, InsideY - 32), new(size + 17, InsideY - 32),
                new(size + 17, InsideY + 10), new(size, InsideY + 10), new(size, InsideY - 21),
                new(0, InsideY - 21), new(0, InsideY + 10)], Sample);
        }
        else if (gauge.Mode == GaugeMode.Depth)
        {
            var end = BeamEnd(pose);
            Face(dc, [new(end, DepthY + 24), new(end, DepthY + 14), new(end + size, DepthY + 14),
                new(end + size, DepthY - 12), new(end + size + 16, DepthY - 12), new(end + size + 16, DepthY + 24)], Sample);
        }
        else if (gauge.Sample == GaugeSample.Rod)
            dc.DrawEllipse(Sample, Outline, new Point(size / 2, 0), size / 2, Math.Min(25, size / 2));
        else
            Box(dc, new Rect(0, -12, size, 24), Sample, 1);
    }

    private static void Micrometer(DrawingContext dc, InstrumentPose pose, MeasurementGauge gauge)
    {
        var unit = Unit(pose);
        var gap = Gap(pose, gauge);
        var lip = SleeveZero + gap;
        var frame = new StreamGeometry();
        using (var path = frame.Open())
        {
            path.BeginFigure(new Point(-16, 0), false, false);
            path.BezierTo(new Point(-112, -3), new Point(-75, 100), new Point(17, 102), true, true);
            path.BezierTo(new Point(94, 103), new Point(118, 53), new Point(108, 7), true, true);
        }
        dc.DrawGeometry(null, Stroke(Shadow, 31), frame);
        dc.DrawGeometry(null, Stroke(Teal, 26), frame);
        Box(dc, new Rect(-28, -11, 28, 22), Steel, 2);
        Box(dc, new Rect(gap, -10, SleeveZero + 4, 20), Steel, 2);
        Box(dc, new Rect(SleeveZero - 16, -24, gap + 36, 48), Steel, 6);
        Cylinder(dc, lip, -34, ThimbleLength, 68, Steel);
        Cylinder(dc, lip + ThimbleLength - 3, -24, 31, 48, DarkSteel);
        Line(dc, SleeveZero - 6, 0, lip, 0, Major);
        for (var half = 0; half * MeasurementGauge.ScrewPitch <= gauge.Opening; half++)
        {
            var x = SleeveZero + half * unit / 2;
            Line(dc, x, 0, x, half % 2 == 0 ? -14 : 9, Tick);
            if (half % 10 == 0)
                Label(dc, (half / 2).ToString(CultureInfo.InvariantCulture), x, -26, 10);
        }
        for (var offset = -10; offset <= 10; offset++)
        {
            var index = gauge.ThimbleTick(-offset);
            var y = Math.Sin(offset * Math.Tau / MeasurementGauge.ScrewPitch) * 30;
            Line(dc, lip + 4, y, lip + (index % 5 == 0 ? 22 : 13), y, Tick);
            if (index % 5 == 0)
                Label(dc, index.ToString(CultureInfo.InvariantCulture), lip + 35, y - 7, 10);
        }
        var shift = gauge.FineDivision % 5;
        for (var x = lip + 64 + shift; x < lip + ThimbleLength - 12; x += 7)
            Line(dc, x, -23, x + 5, 23, Tick);
        DrawSample(dc, pose, gauge);
        Line(dc, 0, -10, 0, 10, DatumPen);
        Line(dc, gap, -10, gap, 10, gauge.AtContact ? Match : DatumPen);
    }

    private static void VernierCloseUp(DrawingContext dc, MeasurementGauge gauge)
    {
        Label(dc, $"VERNIER CALIPER  /  {ModeLabel(gauge.Mode).ToUpperInvariant()} MEASUREMENT", 427, 9, 17);
        var first = Math.Max(0, gauge.MainReading / 100 - 2);
        const double unit = 14.9;
        const double x0 = 28;
        const double axis = 74;
        var mainMatch = gauge.MainReading / 100 + gauge.FineDivision;
        double At(double mm) => x0 + (mm - first) * unit;
        Box(dc, new Rect(21, 40, 810, 79), Dial, 5);
        for (var i = first; i <= Math.Min(first + 53, 199); i++)
        {
            var x = At(i);
            var matching = i == mainMatch && gauge.ShowReading;
            Line(dc, x, axis - 1, x, axis - (i % 5 == 0 ? 18 : 10), matching ? Match : Tick);
            if (matching || ShowScaleLabel(i, mainMatch, gauge.ShowReading))
                Label(dc, i.ToString(CultureInfo.InvariantCulture), x, 42, 13, matching ? Highlight : Scale);
        }
        Line(dc, 22, axis, 830, axis, Major);
        for (var i = 0; i <= MeasurementGauge.VernierDivisions; i++)
        {
            var x = At(gauge.VernierTick(i));
            var matching = i == gauge.FineDivision && gauge.ShowReading;
            Line(dc, x, axis + 1, x, axis + (i % 5 == 0 || matching ? 19 : 10), matching ? Match : i == 0 ? Major : Tick);
            if (matching || ShowScaleLabel(i, gauge.FineDivision, gauge.ShowReading))
                Label(dc, i.ToString(CultureInfo.InvariantCulture), x, 96, 13, matching ? Highlight : Scale);
        }
        Label(dc, "Main scale in mm  ·  Vernier divisions × 0.02 mm", 427, 123, 12);
        Label(dc, gauge.ShowReading ? Reading(gauge) : "Read from vernier zero, then find the matching division.",
            427, 137, 18, gauge.ShowReading ? Highlight : Scale);
    }

    private static bool ShowScaleLabel(int division, int matching, bool highlight) =>
        division % 5 == 0 && (!highlight || Math.Abs(division - matching) > 1);

    private static void MicrometerCloseUp(DrawingContext dc, MeasurementGauge gauge)
    {
        Label(dc, "MICROMETER SCREW GAUGE  /  OUTSIDE MEASUREMENT", 427, 9, 17);
        var first = Math.Max(0, gauge.MainReading / 100 - 4);
        var start = 368 - (gauge.Opening / 100d - first) * 62;
        const double index = 78;
        Box(dc, new Rect(22, 37, 810, 82), Dial, 5);
        Line(dc, 32, index, 368, index, Major);
        for (var half = first * 2; half * MeasurementGauge.ScrewPitch <= gauge.Opening; half++)
        {
            var x = start + (half / 2d - first) * 62;
            var full = half % 2 == 0;
            var matching = gauge.ShowReading && half * MeasurementGauge.ScrewPitch == gauge.MainReading;
            Line(dc, x, index, x, index + (full ? -18 : 16), matching ? Match : Major);
            if (full)
                Label(dc, (half / 2).ToString(CultureInfo.InvariantCulture), x, 42, 15);
        }
        Box(dc, new Rect(368, 39, 189, 78), Steel, 5);
        for (var offset = -5; offset <= 5; offset++)
        {
            var number = gauge.ThimbleTick(-offset);
            var y = index + offset * 7;
            var matching = offset == 0 && gauge.ShowReading;
            Line(dc, 370, y, 370 + (number % 5 == 0 ? 34 : 19), y, matching ? Match : Tick);
            if (number % 5 == 0)
                Label(dc, number.ToString(CultureInfo.InvariantCulture), 427, y - 7, 13);
        }
        Label(dc, "Sleeve  ·  0.50 mm per turn", 693, 55, 14);
        Label(dc, "Thimble  ·  0.01 mm per division", 693, 78, 14);
        Label(dc, gauge.ShowReading ? Reading(gauge) : "Read the last exposed sleeve mark and the thimble at the index.",
            427, 133, 18, gauge.ShowReading ? Highlight : Scale);
    }

    internal static string ModeLabel(GaugeMode mode) => mode switch
    {
        GaugeMode.Inside => "Inside",
        GaugeMode.Depth => "Depth",
        _ => "Outside"
    };

    private static void Cylinder(DrawingContext dc, double x, double y, double width, double height, Brush brush)
    {
        Box(dc, new Rect(x, y, width, height), brush, 8);
        dc.DrawEllipse(DarkSteel, Outline, new Point(x + width, y + height / 2), 6, height / 2);
    }

    private static void Box(DrawingContext dc, Rect rect, Brush brush, double radius) =>
        dc.DrawRoundedRectangle(brush, Outline, rect, radius, radius);

    private static void Face(DrawingContext dc, Point[] points, Brush brush) =>
        dc.DrawGeometry(brush, Outline, BoardSurface.Polyline(points, close: true, fill: true));

    private static void Line(DrawingContext dc, double x1, double y1, double x2, double y2, Pen pen) =>
        dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));

    private static void Label(DrawingContext dc, string text, double x, double y, double size, Brush? brush = null)
    {
        var formatted = BoardSurface.Format(text, size, brush ?? Scale);
        dc.DrawText(formatted, new Point(x - formatted.Width / 2, y));
    }

    private static Brush Paint(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromInvariantString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static Brush Gradient(params (string Hex, double Offset)[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (hex, offset) in colors)
            brush.GradientStops.Add(new GradientStop(((SolidColorBrush)Paint(hex)).Color, offset));
        brush.Freeze();
        return brush;
    }

    private static Pen Stroke(Brush brush, double width)
    {
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}
