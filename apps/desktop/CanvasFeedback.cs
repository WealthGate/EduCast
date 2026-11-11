using System.Windows;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;

namespace EduCast.Studio;

internal sealed class CanvasFeedback : FrameworkElement
{
    private static readonly Brush EraserColor = Brushes.DeepSkyBlue;
    private readonly List<Point> trail = [];
    private Rect marquee = Rect.Empty;
    private Point? eraser;
    private double diameter;
    private double unit;

    public CanvasFeedback() => IsHitTestVisible = false;

    internal void ShowMarquee(Rect area, double scale)
    {
        (marquee, unit) = (area, scale);
        InvalidateVisual();
    }

    internal void ShowEraser(Point? point, double size, double scale, bool sweeping = false)
    {
        (eraser, diameter, unit) = (point, size, scale);
        if (!sweeping)
            trail.Clear();
        else if (point is { } sample)
        {
            if (trail.Count == 32)
                trail.RemoveAt(0);
            trail.Add(sample);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!marquee.IsEmpty)
        {
            var pen = new Pen(Brushes.DodgerBlue, unit) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(38, 30, 144, 255)), pen, marquee);
        }
        if (eraser is not { } point)
            return;
        var pathPen = new Pen(EraserColor, 2 * unit);
        dc.PushOpacity(.55);
        for (var i = 1; i < trail.Count; i++)
            dc.DrawLine(pathPen, trail[i - 1], trail[i]);
        dc.Pop();
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3.5 * unit), point, diameter / 2, diameter / 2);
        dc.DrawEllipse(null, new Pen(Brushes.White, 1.5 * unit), point, diameter / 2, diameter / 2);
    }
}
