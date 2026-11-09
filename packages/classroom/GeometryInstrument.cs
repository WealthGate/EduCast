using System.Collections.Immutable;

namespace EduCast.Classroom;

public enum InstrumentKind { Ruler, Compass, Protractor180, Protractor360, SetSquare45, SetSquare30, FrenchCurve, Calculator }

public readonly record struct GuideEdge(BoardPoint Start, BoardPoint End)
{
    public BoardPoint Closest(BoardPoint point)
    {
        var dx = End.X - Start.X;
        var dy = End.Y - Start.Y;
        var squared = dx * dx + dy * dy;
        var fraction = squared == 0 ? 0 :
            Math.Clamp(((point.X - Start.X) * dx + (point.Y - Start.Y) * dy) / squared, 0, 1);
        return new BoardPoint(Start.X + dx * fraction, Start.Y + dy * fraction);
    }
}

public sealed record InstrumentPose(InstrumentKind Kind, BoardPoint Origin, double Rotation, double Size,
    double Angle, double CurveRise, double UnitsPerCentimetre, bool Inches = false)
{
    public const double DefaultUnitsPerCentimetre = 32;
    public double UnitsPerMeasure => UnitsPerCentimetre * (Inches ? 2.54 : 1);
    public string Unit => Inches ? "in" : "cm";

    public bool IsProtractor => Kind is InstrumentKind.Protractor180 or InstrumentKind.Protractor360;
    public bool IsSetSquare => Kind is InstrumentKind.SetSquare45 or InstrumentKind.SetSquare30;
    public bool CanTrace => Kind is InstrumentKind.Ruler or InstrumentKind.Compass or
        InstrumentKind.SetSquare45 or InstrumentKind.SetSquare30 or InstrumentKind.FrenchCurve;
    public double TriangleHeight => Kind == InstrumentKind.SetSquare30 ? Size / Math.Sqrt(3) : Size;
    public double RulerHeight => Math.Clamp(Size * .18, 48, 72);
    public BoardPoint Hinge => new(Size * .5, -Math.Clamp(Size * .65, 34, 155));

    public static InstrumentPose Create(InstrumentKind kind, double units = DefaultUnitsPerCentimetre,
        bool inches = false) => new(kind, new BoardPoint(455, 376), 0, kind switch
        {
            InstrumentKind.Ruler => 416,
            InstrumentKind.Compass => 126,
            InstrumentKind.Protractor180 or InstrumentKind.Protractor360 => 154,
            InstrumentKind.SetSquare45 or InstrumentKind.SetSquare30 => 220,
            InstrumentKind.FrenchCurve => 380,
            _ => 300
        }, 60, 132, units, inches);

    public BoardPoint ToBoard(BoardPoint local)
    {
        var rotated = Rotate(local, Rotation);
        return new BoardPoint(Origin.X + rotated.X, Origin.Y + rotated.Y, local.Pressure);
    }

    public BoardPoint ToLocal(BoardPoint board) =>
        Rotate(new BoardPoint(board.X - Origin.X, board.Y - Origin.Y, board.Pressure), -Rotation);

    public ImmutableArray<GuideEdge> Edges() => Kind switch
    {
        InstrumentKind.Ruler => [new(new(0, 0), new(Size, 0))],
        InstrumentKind.SetSquare45 or InstrumentKind.SetSquare30 =>
        [
            new(new(0, 0), new(Size, 0)), new(new(Size, 0), new(0, -TriangleHeight)),
            new(new(0, -TriangleHeight), new(0, 0))
        ],
        _ => []
    };

    public BoardPoint CurveAt(double x)
    {
        x = Math.Clamp(x, -Size / 2, Size / 2);
        return new BoardPoint(x, -CurveRise * Math.Pow(2 * x / Size, 2));
    }

    public ImmutableArray<BoardPoint> TraceCurve(double firstX, double lastX)
    {
        const int segments = 96;
        var first = CurveAt(firstX).X;
        var last = CurveAt(lastX).X;
        return Enumerable.Range(0, segments + 1)
            .Select(i => ToBoard(CurveAt(first + (last - first) * i / segments))).ToImmutableArray();
    }

    public double ReadAngle(BoardPoint pointer)
    {
        var local = ToLocal(pointer);
        var degrees = Normalize(-Direction(local));
        if (Kind == InstrumentKind.Protractor180 && degrees > 180)
            return degrees < 270 ? 180 : 0;
        return degrees;
    }

    public static BoardPoint Polar(double degrees, double radius)
    {
        var radians = degrees * Math.PI / 180;
        return new BoardPoint(Math.Cos(radians) * radius, Math.Sin(radians) * radius);
    }

    public static double Direction(BoardPoint offset) => Math.Atan2(offset.Y, offset.X) * 180 / Math.PI;

    public static double Bearing(BoardPoint origin, BoardPoint end) =>
        Direction(new BoardPoint(end.X - origin.X, end.Y - origin.Y));

    public static double Distance(BoardPoint first, BoardPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    public static double Normalize(double degrees) => (degrees % 360 + 360) % 360;

    public static double SweepStep(double previous, double next) => Normalize(next - previous + 180) - 180;

    private static BoardPoint Rotate(BoardPoint point, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new BoardPoint(point.X * cosine - point.Y * sine, point.X * sine + point.Y * cosine, point.Pressure);
    }
}
