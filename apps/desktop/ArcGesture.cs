using EduCast.Classroom;

namespace EduCast.Studio;

internal sealed class ArcGesture(BoardPoint center, BoardPoint start)
{
    private readonly double startBearing = InstrumentPose.Bearing(center, start);
    private readonly double radius = InstrumentPose.Distance(center, start);
    private double previousBearing = InstrumentPose.Bearing(center, start);
    private double travel;
    private double low;
    private double high;

    internal double Bearing => previousBearing;

    internal void Follow(BoardPoint pointer)
    {
        // There is no useful bearing near the pivot.
        if (InstrumentPose.Distance(center, pointer) < Math.Min(8, radius / 2))
            return;
        var bearing = InstrumentPose.Bearing(center, pointer);
        travel += InstrumentPose.SweepStep(previousBearing, bearing);
        previousBearing = bearing;
        if (high - low >= 360)
            return;
        low = Math.Min(low, travel);
        high = Math.Max(high, travel);
    }

    internal Shape Apply(Shape arc)
    {
        // Retracing keeps the visible ink, even when crossing back through the start.
        var direction = high >= -low ? 1 : -1;
        var sweep = Math.Min(360, high - low) * direction;
        var offset = InstrumentPose.Polar(startBearing + (direction > 0 ? low : high), radius);
        return arc with { End = new BoardPoint(center.X + offset.X, center.Y + offset.Y), Sweep = sweep };
    }
}
