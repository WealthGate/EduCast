using System.Windows;
using System.Windows.Media;

namespace EduCast.Studio;

internal static class CanvasClose
{
    private static readonly Brush Fill = new SolidColorBrush(Color.FromRgb(174, 39, 68));

    internal static Point Center(Rect bounds, double unit) => new(
        Math.Clamp(bounds.Right - 10 * unit, 12 * unit, BoardSurface.BoardWidth - 12 * unit),
        Math.Clamp(bounds.Top - 14 * unit, 12 * unit, BoardSurface.BoardHeight - 12 * unit));

    internal static bool Hit(Rect bounds, Point pointer, double unit) =>
        !bounds.IsEmpty && (pointer - Center(bounds, unit)).Length <= 11 * unit;

    internal static void Draw(DrawingContext dc, Rect bounds, double unit)
    {
        if (bounds.IsEmpty)
            return;
        var center = Center(bounds, unit);
        dc.DrawEllipse(Fill, new Pen(Brushes.White, 1.5 * unit), center, 10 * unit, 10 * unit);
        var pen = new Pen(Brushes.White, 1.8 * unit);
        dc.DrawLine(pen, center + new Vector(-3.5, -3.5) * unit, center + new Vector(3.5, 3.5) * unit);
        dc.DrawLine(pen, center + new Vector(-3.5, 3.5) * unit, center + new Vector(3.5, -3.5) * unit);
    }
}
