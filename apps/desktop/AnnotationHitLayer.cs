using System.Windows;
using System.Windows.Media;

namespace EduCast.Studio;

internal sealed class AnnotationHitLayer : FrameworkElement
{
    internal Func<Point, bool>? ContainsAnnotation { get; set; }

    protected override void OnRender(DrawingContext context) =>
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) =>
        ContainsAnnotation?.Invoke(parameters.HitPoint) == true
            ? new PointHitTestResult(this, parameters.HitPoint) : null;
}
