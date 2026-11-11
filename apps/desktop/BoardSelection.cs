using System.Collections.Immutable;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EduCast.Classroom;
using ItemVisibility = EduCast.Classroom.Visibility;
using Matrix = System.Windows.Media.Matrix;

namespace EduCast.Studio;

internal sealed class BoardSelection
{
    private readonly Rect bounds;
    private readonly Point start;
    private readonly Point pivot;
    private readonly Vector axis;
    private readonly double unit;

    public ImmutableArray<Content> Items { get; }
    public Content Item => Items[0];
    public Cursor Cursor { get; }

    private BoardSelection(ImmutableArray<Content> items, Point start, double unit, Vector edge)
    {
        Items = items;
        this.start = start;
        this.unit = unit;
        bounds = BoundsOf(items);
        Cursor = CursorFor(edge);
        if (edge.LengthSquared == 0)
            return;
        var visible = Rect.Intersect(bounds, BoardSurface.Bounds);
        pivot = At(visible, -edge);
        var outline = Outline(bounds, unit);
        axis = At(outline, edge) - At(outline, -edge);
    }

    internal static double Unit(double renderedWidth) =>
        renderedWidth > 0 ? Math.Clamp(BoardSurface.BoardWidth / renderedWidth, .25, 5) : 1;

    internal static BoardSelection? Begin(IEnumerable<Content> items, Content? selected, Point point, double unit)
    {
        if (selected is not null)
        {
            var edge = EdgeAt(BoardSurface.BoundsOf(selected), point, unit);
            if (edge.LengthSquared > 0)
                return new BoardSelection([selected], point, unit, edge);
        }
        var hit = BoardSurface.Hit(items, point);
        if (hit is null && selected is not null && Outline(BoardSurface.BoundsOf(selected), unit).Contains(point))
            hit = selected;
        hit ??= HitBounds(items, point);
        return hit is null ? null : new BoardSelection([hit], point, unit, default);
    }

    internal static BoardSelection? BeginSelected(ImmutableArray<Content> items, Point point, double unit)
    {
        var bounds = BoundsOf(items);
        var edge = EdgeAt(bounds, point, unit);
        return edge.LengthSquared > 0 || Outline(bounds, unit).Contains(point)
            ? new BoardSelection(items, point, unit, edge) : null;
    }

    internal static ImmutableArray<Content> Within(IEnumerable<Content> items, Point start, Point end, double unit)
    {
        if ((end - start).Length < 3 * unit)
            return [];
        var area = new Rect(start, end);
        return items.Where(item => item.Visibility is ItemVisibility.Both or ItemVisibility.TeacherOnly &&
            Rect.Intersect(BoardSurface.BoundsOf(item), BoardSurface.Bounds) is { IsEmpty: false } visible &&
            area.Contains(visible)).ToImmutableArray();
    }

    internal static Rect BoundsOf(IEnumerable<Content> items)
    {
        var bounds = Rect.Empty;
        foreach (var item in items)
            bounds.Union(BoardSurface.BoundsOf(item));
        return bounds;
    }

    internal static ImmutableArray<Content> Apply(ImmutableArray<Content> items, ImmutableArray<Content> replacements)
    {
        if (replacements.IsEmpty)
            return items;
        var byId = replacements.ToDictionary(item => item.Id);
        return items.Select(item => byId.GetValueOrDefault(item.Id, item)).ToImmutableArray();
    }

    internal static Content? Hit(IEnumerable<Content> items, Point point) =>
        BoardSurface.Hit(items, point) ?? HitBounds(items, point);

    private static Content? HitBounds(IEnumerable<Content> items, Point point) => items.Reverse().FirstOrDefault(item =>
        item is not Ink && item.Visibility is ItemVisibility.Both or ItemVisibility.TeacherOnly &&
        BoardSurface.BoundsOf(item).Contains(point));

    internal static Cursor Hover(Content? selected, Point point, double unit) =>
        Hover(selected is null ? Rect.Empty : BoardSurface.BoundsOf(selected), point, unit);

    internal static Cursor Hover(Rect bounds, Point point, double unit)
    {
        if (bounds.IsEmpty)
            return Cursors.Arrow;
        if (CloseAt(bounds, point, unit))
            return Cursors.Hand;
        var edge = EdgeAt(bounds, point, unit);
        return edge.LengthSquared > 0 ? CursorFor(edge) : Outline(bounds, unit).Contains(point) ? Cursors.SizeAll : Cursors.Arrow;
    }

    internal Matrix Preview(Point point)
    {
        var delta = point - start;
        if (delta.Length < 3 * unit)
            return Matrix.Identity;
        if (axis.LengthSquared == 0)
        {
            var offset = BoardSurface.ConstrainMove(bounds, delta);
            return new Matrix(1, 0, 0, 1, offset.X, offset.Y);
        }

        var minimum = Math.Max(Items.Max(item => ObjectPlacement.MinimumScale / (item.Placement?.Scale ?? 1)),
            Math.Min(1, 12 * unit / Math.Max(bounds.Width, bounds.Height)));
        var maximum = Math.Min(Items.Min(item => ObjectPlacement.MaximumScale / (item.Placement?.Scale ?? 1)), GrowthLimit());
        var factor = Math.Clamp(1 + Vector.Multiply(delta, axis) / axis.LengthSquared, minimum, maximum);
        return new Matrix(factor, 0, 0, factor, pivot.X * (1 - factor), pivot.Y * (1 - factor));
    }

    internal Content Commit(Point point) => CommitAll(point)[0];

    internal ImmutableArray<Content> CommitAll(Point point)
    {
        var transform = Preview(point);
        if (transform.IsIdentity)
            return Items;
        if (Items.Length == 1 && axis.LengthSquared == 0)
            return [BoardSurface.Move(Item, point - start)];
        return Items.Select(item =>
        {
            var placement = item.Placement ?? new ObjectPlacement(1, 0, 0);
            var offset = transform.Transform(new Point(placement.X, placement.Y));
            return item with { Placement = new ObjectPlacement(placement.Scale * transform.M11, offset.X, offset.Y) };
        }).ToImmutableArray();
    }

    private double GrowthLimit()
    {
        static double Limit(double space, double extent) => extent > 0 ? space / extent : double.PositiveInfinity;
        return Math.Max(1, Math.Min(
            Math.Min(Limit(pivot.X, pivot.X - bounds.Left), Limit(BoardSurface.BoardWidth - pivot.X, bounds.Right - pivot.X)),
            Math.Min(Limit(pivot.Y, pivot.Y - bounds.Top), Limit(BoardSurface.BoardHeight - pivot.Y, bounds.Bottom - pivot.Y))));
    }

    private static Rect Outline(Rect bounds, double unit)
    {
        var visible = Rect.Intersect(bounds, BoardSurface.Bounds);
        if (visible.IsEmpty)
            return Rect.Empty;
        const double padding = 8;
        var width = Math.Min(BoardSurface.BoardWidth - 10 * unit, Math.Max(visible.Width + 2 * padding * unit, 34 * unit));
        var height = Math.Min(BoardSurface.BoardHeight - 10 * unit, Math.Max(visible.Height + 2 * padding * unit, 34 * unit));
        var inset = 5 * unit;
        var left = Math.Clamp(visible.Left + (visible.Width - width) / 2, inset,
            Math.Max(inset, BoardSurface.BoardWidth - width - inset));
        var top = Math.Clamp(visible.Top + (visible.Height - height) / 2, inset,
            Math.Max(inset, BoardSurface.BoardHeight - height - inset));
        return new Rect(left, top, width, height);
    }

    private static Vector EdgeAt(Rect bounds, Point point, double unit)
    {
        var outline = Outline(bounds, unit);
        if (outline.IsEmpty)
            return default;
        var tolerance = 6 * unit;
        var reach = outline;
        reach.Inflate(tolerance, tolerance);
        if (!reach.Contains(point))
            return default;
        int Near(double position, double first, double last) => Math.Abs(position - first) <= tolerance ? -1 :
            Math.Abs(position - last) <= tolerance ? 1 : 0;
        return new Vector(Near(point.X, outline.Left, outline.Right), Near(point.Y, outline.Top, outline.Bottom));
    }

    private static Point At(Rect bounds, Vector edge) =>
        new(bounds.Left + bounds.Width * (edge.X + 1) / 2, bounds.Top + bounds.Height * (edge.Y + 1) / 2);

    private static Cursor CursorFor(Vector edge) => (edge.X, edge.Y) switch
    {
        (0, 0) => Cursors.SizeAll,
        (0, _) => Cursors.SizeNS,
        (_, 0) => Cursors.SizeWE,
        _ => edge.X == edge.Y ? Cursors.SizeNWSE : Cursors.SizeNESW
    };

    internal static bool CloseAt(Content selected, Point point, double unit) =>
        CloseAt(BoardSurface.BoundsOf(selected), point, unit);

    internal static bool CloseAt(Rect bounds, Point point, double unit) =>
        CanvasClose.Hit(Outline(bounds, unit), point, unit);

    internal static void Draw(DrawingContext dc, Rect bounds, double unit)
    {
        var outline = Outline(bounds, unit);
        if (outline.IsEmpty)
            return;
        var pen = new Pen(Brushes.DodgerBlue, 1.4 * unit);
        dc.DrawRectangle(null, pen, outline);
        for (var y = -1; y <= 1; y++)
        {
            for (var x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0)
                    continue;
                var point = At(outline, new Vector(x, y));
                dc.DrawRectangle(Brushes.White, pen, new Rect(point.X - 4 * unit, point.Y - 4 * unit, 8 * unit, 8 * unit));
            }
        }
        CanvasClose.Draw(dc, outline, unit);
    }
}
