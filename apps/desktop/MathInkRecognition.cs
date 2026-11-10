using System.Windows;
using EduCast.Classroom;

namespace EduCast.Studio;

internal sealed record MathSketch(string Label, string Source, Rect Bounds, bool IsSymbol = false);

internal static class MathInkRecognition
{
    private sealed record Mark(Point[] Points, Rect Bounds)
    {
        internal double X => Bounds.Left + Bounds.Width / 2;
        internal double Y => Bounds.Top + Bounds.Height / 2;
    }

    // Each pair is an x/y coordinate on a small grid. A slash starts a new pen stroke.
    private static readonly (char Name, Point[] Points)[] Glyphs =
    [
        ('0', Template("50 21 02 05 17 39 69 87 95 92 71 50")),
        ('1', Template("13 41 49")), ('1', Template("50 59")), ('1', Template("14 41 49 / 09 89")),
        ('2', Template("02 20 60 81 83 65 29 89")),
        ('3', Template("01 60 81 83 55 35 65 87 88 69 29 08")),
        ('4', Template("60 05 85 / 60 69")), ('4', Template("20 04 85 / 71 79")),
        ('5', Template("80 10 04 44 74 86 87 69 29 08")),
        ('6', Template("80 41 13 05 07 39 69 87 85 64 34 05")),
        ('7', Template("00 90 39")), ('7', Template("00 90 39 / 25 85")),
        ('8', Template("51 21 03 24 74 95 87 69 39 07 05 24 74 93 91 51")),
        ('9', Template("86 81 60 20 02 04 35 65 84 86 69 39")),
        ('a', Template("81 61 31 12 04 06 29 49 77 84 81 89")),
        ('b', Template("10 19 59 87 85 65 35 17")),
        ('c', Template("81 41 12 05 17 49 79")),
        ('e', Template("15 85 82 60 30 12 05 17 49 89")),
        ('m', Template("09 01 25 39 31 55 69 61 85 99")),
        ('n', Template("09 01 25 51 81 89")), ('n', Template("09 02 22 62 81 89")),
        ('x', Template("00 99 / 90 09")), ('x', Template("01 98 / 90 19")),
        ('y', Template("00 55 90 / 55 29")), ('y', Template("00 55 80 59 39")),
        ('z', Template("00 90 09 99")),
        ('+', Template("05 95 / 50 59")), ('-', Template("05 95"))
    ];

    internal static (MathSketch Math, int Count)? Recognize(IReadOnlyList<Ink> strokes)
    {
        var marks = strokes.Select(stroke =>
        {
            var points = SketchRecognition.Sample(stroke);
            return new Mark(points, SketchRecognition.Bounds(points));
        }).ToArray();
        for (var start = 0; start < marks.Length; start++)
        {
            var group = marks[start..];
            if (Read(group) is { } result)
                return (result, group.Length);
        }
        return null;
    }

    private static MathSketch? Read(Mark[] marks)
    {
        var bounds = Bounds(marks);
        foreach (var bar in marks.Where(IsBar).OrderByDescending(mark => mark.Bounds.Width))
        {
            var width = bar.Bounds.Width;
            var top = marks.Where(mark => mark != bar && mark.Bounds.Bottom < bar.Y - 2).ToArray();
            var bottom = marks.Where(mark => mark != bar && mark.Bounds.Top > bar.Y + 2).ToArray();
            if (top.Length == 0 || bottom.Length == 0 || top.Length + bottom.Length + 1 != marks.Length ||
                marks.Any(mark => mark != bar && (mark.Bounds.Left < bar.Bounds.Left - width * .18 ||
                    mark.Bounds.Right > bar.Bounds.Right + width * .18 || Math.Abs(mark.Y - bar.Y) > width * 1.25)))
                continue;
            if (top.Length == 1 && bottom.Length == 1 && IsDot(top[0]) && IsDot(bottom[0]) &&
                Math.Abs(top[0].X - bar.X) < width * .22 && Math.Abs(bottom[0].X - bar.X) < width * .22)
                return new MathSketch("division sign", "÷", bounds, IsSymbol: true);
            if (Bounds(top).Height >= 10 && Bounds(bottom).Height >= 10)
                return new MathSketch("fraction", $"({ReadTerm(top)})/({ReadTerm(bottom)})", bounds);
        }

        foreach (var root in marks)
        {
            var roof = Radical(root, marks);
            if (roof is not { } radical)
                continue;
            var other = marks.Where(mark => mark != root && mark != radical.Bar).ToArray();
            var underneath = other.Where(mark => mark.X > radical.Start.X - 4 &&
                mark.X < radical.End.X + 6 && mark.Bounds.Top >= radical.Start.Y - 4 &&
                mark.Bounds.Bottom <= root.Bounds.Bottom + root.Bounds.Height * .35).ToArray();
            var indices = other.Where(mark => mark.X < radical.Start.X &&
                mark.Bounds.Left >= root.Bounds.Left - root.Bounds.Height * .68 &&
                mark.Bounds.Height <= root.Bounds.Height * .78 && mark.Y < root.Y).ToArray();
            if (underneath.Length + indices.Length != other.Length || underneath.Intersect(indices).Any())
                continue;
            var term = ReadTerm(underneath);
            if (indices.Length == 0)
                return new MathSketch("square root", $"sqrt({term})", bounds);
            var index = ReadTerm(indices);
            return new MathSketch(index == "3" ? "cube root" : "indexed root", $"root({index},{term})", bounds);
        }

        if (marks.Length < 2)
            return null;
        var bases = marks.Where(mark => mark.Bounds.Left < bounds.Left + Math.Max(5, bounds.Width * .11)).ToList();
        var basis = Bounds(bases);
        foreach (var candidate in marks.Except(bases).OrderBy(mark => mark.Bounds.Left).ToArray())
        {
            if (candidate.Bounds.Height >= basis.Height * .82 &&
                Math.Abs(candidate.Y - basis.Top - basis.Height / 2) <= basis.Height * .22 &&
                candidate.Bounds.Left <= basis.Right + basis.Height * .42)
            {
                bases.Add(candidate);
                basis.Union(candidate.Bounds);
            }
        }
        var right = marks.Except(bases).ToArray();
        if (basis.Height < 18 || right.Length == 0 || right.Any(mark =>
            mark.Bounds.Left < basis.Right - basis.Width * .24 ||
            mark.Bounds.Left > basis.Right + basis.Height * .8 ||
            mark.Bounds.Height > basis.Height * .81))
            return null;
        var above = right.Where(mark => mark.Y <= basis.Top + basis.Height * .37).ToArray();
        var below = right.Where(mark => mark.Y >= basis.Top + basis.Height * .74).ToArray();
        if (above.Length + below.Length != right.Length)
            return null;
        var expression = ReadTerm(bases.ToArray());
        if (below.Length > 0)
            expression += $"_({ReadTerm(below)})";
        if (above.Length > 0)
            expression += $"^({ReadTerm(above)})";
        return new MathSketch(above.Length == 0 ? "subscript" : below.Length == 0 ? "superscript" : "scripts", expression, bounds);
    }

    private static bool IsBar(Mark mark) => mark.Bounds.Width >= 24 && mark.Bounds.Height <= mark.Bounds.Width * .13 &&
        Math.Abs(mark.Points[0].X - mark.Points[^1].X) > mark.Bounds.Width * .8;

    private static bool IsDot(Mark mark) => Math.Max(mark.Bounds.Width, mark.Bounds.Height) <= 11;

    private static (Point Start, Point End, Mark? Bar)? Radical(Mark mark, Mark[] marks)
    {
        var box = mark.Bounds;
        if (mark.Points.Length < 3 || box.Width < 12 || box.Height < 18)
            return null;
        var points = SketchRecognition.Simplify(mark.Points, Math.Max(2, box.Height * .09));
        if (points[0].X > points[^1].X)
            Array.Reverse(points);
        if (points.Length is < 3 or > 4)
            return null;
        var (first, bottom, roof) = (points[0], points[1], points[2]);
        if (bottom.Y - first.Y <= box.Height * .16 || bottom.Y - roof.Y <= box.Height * .72 ||
            roof.X <= bottom.X || bottom.X < first.X - 3)
            return null;
        if (points.Length == 4)
        {
            var end = points[3];
            return end.X - roof.X > box.Width * .42 && Math.Abs(end.Y - roof.Y) < box.Height * .2 ? (roof, end, null) : null;
        }
        var bar = marks.FirstOrDefault(other => other != mark && IsBar(other) &&
            Math.Abs(other.Bounds.Left - roof.X) < Math.Max(5, box.Height * .22) &&
            Math.Abs(other.Y - roof.Y) < Math.Max(5, box.Height * .14));
        return bar is null ? null : (roof, new Point(bar.Bounds.Right, bar.Y), bar);
    }

    private static string ReadTerm(Mark[] marks)
    {
        if (marks.Length == 0)
            return "?";
        var glyphs = new List<List<Mark>>();
        var right = double.NegativeInfinity;
        foreach (var mark in marks.OrderBy(mark => mark.Bounds.Left))
        {
            if (glyphs.Count == 0 || mark.Bounds.Left > right + 1)
            {
                glyphs.Add([]);
                right = double.NegativeInfinity;
            }
            glyphs[^1].Add(mark);
            right = Math.Max(right, mark.Bounds.Right);
        }
        return new string(glyphs.Select(ReadGlyph).ToArray());
    }

    private static char ReadGlyph(List<Mark> marks)
    {
        var box = Bounds(marks);
        if (box.Height >= 10 && box.Width < box.Height * .19)
            return '1';
        if (marks.Count == 1 && IsBar(marks[0]))
            return '-';
        if (box.Height < 8 || marks.Count > 3)
            return '?';
        var input = Normalize(marks.Select(mark => mark.Points));
        var choices = Glyphs.GroupBy(template => template.Name).Select(group =>
            (Name: group.Key, Error: group.Min(template => Error(input, template.Points)))).OrderBy(choice => choice.Error).ToArray();
        return choices[0].Error < .145 && choices[1].Error - choices[0].Error > .012 ? choices[0].Name : '?';
    }

    private static double Error(Point[] a, Point[] b) =>
        (a.Average(point => b.Min(other => (point - other).Length)) +
            b.Average(point => a.Min(other => (point - other).Length))) / 2;

    private static Point[] Template(string path) => Normalize(path.Split('/').Select(stroke =>
        stroke.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(token => new Point(token[0] - '0', token[1] - '0')).ToArray()));

    private static Point[] Normalize(IEnumerable<Point[]> paths)
    {
        var strokes = paths.ToArray();
        var box = SketchRecognition.Bounds(strokes.SelectMany(stroke => stroke).ToArray());
        var scale = Math.Max(1, Math.Max(box.Width, box.Height));
        var center = box.TopLeft + new Vector(box.Width / 2, box.Height / 2);
        var points = new List<Point>();
        foreach (var stroke in strokes)
        {
            points.Add(stroke[0]);
            for (var i = 1; i < stroke.Length; i++)
            {
                var delta = stroke[i] - stroke[i - 1];
                var steps = Math.Max(1, (int)Math.Ceiling(delta.Length / (scale * .065)));
                for (var step = 1; step <= steps; step++)
                    points.Add(stroke[i - 1] + delta * (step / (double)steps));
            }
        }
        return points.Select(point => new Point((point.X - center.X) / scale, (point.Y - center.Y) / scale)).ToArray();
    }

    private static Rect Bounds(IEnumerable<Mark> marks)
    {
        var bounds = Rect.Empty;
        foreach (var mark in marks)
            bounds.Union(mark.Bounds);
        return bounds;
    }
}
