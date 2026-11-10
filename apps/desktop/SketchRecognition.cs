using System.Windows;
using EduCast.Classroom;

namespace EduCast.Studio;

internal static class SketchRecognition
{
    internal static Shape? Recognize(Ink ink, Ink? previous, BoardSubject subject)
    {
        var points = Sample(ink);
        if (points.Length < 2)
            return null;
        var bounds = Bounds(points);
        var size = Math.Max(bounds.Width, bounds.Height);
        if (size < 24)
            return null;
        var corners = Simplify(points, Math.Max(3, size * .045));
        var start = points[0];
        var end = points[^1];

        Shape Make(ShapeKind kind, Point from, Point to, Point? corner = null) =>
            new(ink.Id, ink.Visibility, ink.IsAnswer, ink.Argb, ink.Width, kind, Board(from), Board(to))
            {
                Corner = corner is { } point ? Board(point) : null
            };

        if (previous is not null && corners.Length == 2 &&
            previous.Argb == ink.Argb && previous.Width == ink.Width &&
            previous.Visibility == ink.Visibility && previous.IsAnswer == ink.IsAnswer &&
            Parallel(Sample(previous), start, end) is { } pair)
        {
            if (subject == BoardSubject.Chemistry)
                return Make(ShapeKind.DoubleBond, pair.Start, pair.End);
            if (pair.Horizontal)
                return Make(ShapeKind.Equal, pair.Start, pair.End);
            if (subject == BoardSubject.Physics)
            {
                var center = pair.Start + (pair.End - pair.Start) * .5;
                var half = (pair.End - pair.Start).Length * 1.2;
                return Make(ShapeKind.Capacitor, center - new Vector(half, 0), center + new Vector(half, 0));
            }
        }

        if (corners.Length == 2 && (end - start).Length > size * .85 &&
            PathLength(points) <= (end - start).Length * 1.25)
            return Make(ShapeKind.Line, start, end);
        if (corners.Length == 5 && (corners[1] - corners[3]).Length < size * .12 &&
            ArrowHead(start, corners[1], corners[2], corners[4]))
            return Make(ShapeKind.Arrow, start, corners[1]);
        if (subject == BoardSubject.Physics && IsResistor(corners))
            return Make(ShapeKind.Resistor, start, end);

        if ((end - start).Length > size * .20 || Math.Min(bounds.Width, bounds.Height) < 18)
            return null;
        var loop = corners[..^1].ToList();
        // A stroke may begin halfway along a polygon edge.
        if (loop.Count >= 3 && (end - start).Length < size * .13 &&
            DistanceToSegment(start, loop[^1], loop[1]) < size * .055)
            loop.RemoveAt(0);
        if (loop.Count == 3 && PolygonArea(loop) > bounds.Width * bounds.Height * .26)
            return Make(ShapeKind.Triangle, loop[0], loop[1], loop[2]);
        if (loop.Count == 4 && IsRectangle(loop))
        {
            var firstEdge = loop[1] - loop[0];
            firstEdge.Normalize();
            var normal = new Vector(-firstEdge.Y, firstEdge.X);
            var horizontal = Vector.Multiply((loop[1] - loop[0]) + (loop[2] - loop[3]), firstEdge) / 2;
            var vertical = Vector.Multiply((loop[3] - loop[0]) + (loop[2] - loop[1]), normal) / 2;
            return Make(ShapeKind.Rectangle, loop[0], loop[0] + firstEdge * horizontal,
                loop[0] + normal * vertical);
        }
        if (!IsEllipse(points, bounds))
            return null;
        var centerOfBounds = bounds.TopLeft + new Vector(bounds.Width / 2, bounds.Height / 2);
        return Math.Min(bounds.Width, bounds.Height) >= size * .83
            ? Make(ShapeKind.Circle, centerOfBounds, centerOfBounds + new Vector((bounds.Width + bounds.Height) / 4, 0))
            : Make(ShapeKind.Ellipse, bounds.TopLeft, bounds.BottomRight);
    }

    private static (bool Horizontal, Point Start, Point End)? Parallel(Point[] prior, Point a, Point b)
    {
        if (prior.Length < 2)
            return null;
        var p = prior[0];
        var q = prior[^1];
        var length = (b - a).Length;
        var priorLength = (q - p).Length;
        if (priorLength < length * .72 || priorLength > length * 1.38 ||
            Simplify(prior, length * .065).Length != 2 || PathLength(prior) > priorLength * 1.25)
            return null;
        var horizontal = Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y);
        Point Along(Point point) => horizontal ? point : new Point(point.Y, point.X);
        (a, b, p, q) = (Along(a), Along(b), Along(p), Along(q));
        if (Math.Abs(b.Y - a.Y) > length * .19 || Math.Abs(q.Y - p.Y) > length * .19)
            return null;
        if (a.X > b.X)
            (a, b) = (b, a);
        if (p.X > q.X)
            (p, q) = (q, p);
        var gap = Math.Abs((p.Y + q.Y - a.Y - b.Y) / 2);
        if (gap < length * .10 || gap > length * .58 ||
            Math.Abs(p.X - a.X) > length * .25 || Math.Abs(q.X - b.X) > length * .25)
            return null;
        return (horizontal, Along(new((a.X + p.X) / 2, (a.Y + p.Y) / 2)),
            Along(new((b.X + q.X) / 2, (b.Y + q.Y) / 2)));
    }

    private static bool ArrowHead(Point tail, Point tip, Point wingA, Point wingB)
    {
        var shaft = tip - tail;
        var length = shaft.Length;
        if (length < 28)
            return false;
        shaft.Normalize();
        var a = wingA - tip;
        var b = wingB - tip;
        var sideA = Vector.CrossProduct(shaft, a);
        var sideB = Vector.CrossProduct(shaft, b);
        return a.Length < length * .55 && b.Length < length * .55 && sideA * sideB < 0 &&
            Math.Abs(sideA) > length * .06 && Math.Abs(sideB) > length * .06 &&
            Vector.Multiply(a, shaft) < -length * .08 && Vector.Multiply(b, shaft) < -length * .08;
    }

    private static bool IsResistor(Point[] corners)
    {
        if (corners.Length is < 6 or > 11)
            return false;
        var axis = corners[^1] - corners[0];
        var length = axis.Length;
        if (length < 42)
            return false;
        axis.Normalize();
        var previous = 0d;
        var sign = 0;
        var peaks = 0;
        foreach (var corner in corners[1..^1])
        {
            var delta = corner - corners[0];
            var forward = Vector.Multiply(axis, delta);
            var side = Vector.CrossProduct(axis, delta);
            if (forward < previous + length * .025 || forward >= length || Math.Abs(side) > length * .26)
                return false;
            previous = forward;
            if (Math.Abs(side) < length * .035)
                continue;
            if (Math.Sign(side) == sign)
                return false;
            sign = Math.Sign(side);
            peaks++;
        }
        return peaks >= 4;
    }

    private static double PolygonArea(IReadOnlyList<Point> corners)
    {
        var area = 0d;
        for (var i = 0; i < corners.Count; i++)
            area += Vector.CrossProduct((Vector)corners[i], (Vector)corners[(i + 1) % corners.Count]);
        return Math.Abs(area) / 2;
    }

    private static bool IsRectangle(IReadOnlyList<Point> corners)
    {
        for (var i = 0; i < 4; i++)
        {
            var left = corners[(i + 3) % 4] - corners[i];
            var right = corners[(i + 1) % 4] - corners[i];
            if (left.Length < 15 || right.Length < 15 ||
                Math.Abs(Vector.Multiply(left, right)) > left.Length * right.Length * .35)
                return false;
        }
        return true;
    }

    private static bool IsEllipse(Point[] points, Rect bounds)
    {
        var rx = bounds.Width / 2;
        var ry = bounds.Height / 2;
        var previous = 0d;
        var angleTravel = 0d;
        var rotation = 0d;
        var error = 0d;
        for (var i = 0; i < points.Length; i++)
        {
            var x = (points[i].X - bounds.Left - rx) / rx;
            var y = (points[i].Y - bounds.Top - ry) / ry;
            var deviation = Math.Abs(Math.Sqrt(x * x + y * y) - 1);
            if (deviation > .29)
                return false;
            error += deviation;
            var angle = Math.Atan2(y, x);
            if (i > 0)
            {
                var step = Math.IEEERemainder(angle - previous, Math.Tau);
                angleTravel += Math.Abs(step);
                rotation += step;
            }
            previous = angle;
        }
        return points.Length >= 8 && error / points.Length < .115 &&
            Math.Abs(rotation) is > 5.5 and < 6.8 && angleTravel - Math.Abs(rotation) < .65;
    }

    private static Point[] Sample(Ink ink)
    {
        var count = Math.Min(ink.Points.Length, 256);
        var points = new Point[count];
        for (var i = 0; i < count; i++)
        {
            var point = ink.Points[i * (ink.Points.Length - 1) / Math.Max(1, count - 1)];
            points[i] = new Point(point.X, point.Y);
        }
        return points;
    }

    private static Rect Bounds(Point[] points) =>
        new(new Point(points.Min(p => p.X), points.Min(p => p.Y)),
            new Point(points.Max(p => p.X), points.Max(p => p.Y)));

    private static BoardPoint Board(Point point) => new(point.X, point.Y);

    private static double PathLength(Point[] points)
    {
        var length = 0d;
        for (var i = 1; i < points.Length; i++)
            length += (points[i] - points[i - 1]).Length;
        return length;
    }

    private static Point[] Simplify(Point[] points, double tolerance)
    {
        var corners = new List<Point> { points[0] };
        AddCorners(0, points.Length - 1);
        corners.Add(points[^1]);
        return corners.ToArray();

        void AddCorners(int first, int last)
        {
            var farthest = -1;
            var error = tolerance;
            for (var i = first + 1; i < last; i++)
            {
                var distance = DistanceToSegment(points[i], points[first], points[last]);
                if (distance > error)
                {
                    farthest = i;
                    error = distance;
                }
            }
            if (farthest < 0)
                return;
            AddCorners(first, farthest);
            corners.Add(points[farthest]);
            AddCorners(farthest, last);
        }
    }

    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        var segment = b - a;
        if (segment.LengthSquared == 0)
            return (point - a).Length;
        var t = Math.Clamp(Vector.Multiply(point - a, segment) / segment.LengthSquared, 0, 1);
        return (point - (a + t * segment)).Length;
    }
}
