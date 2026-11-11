using System.Collections.Immutable;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EduCast.Classroom;

namespace EduCast.Studio;

internal sealed record SourceInk(ImmutableArray<Content> Marks, Content? Gesture = null, Point? Pointer = null);
internal sealed record SourceFrame(BitmapSource Image, SourceInk Ink, Guid? MediaId = null);
internal sealed record AnnotationSelection(Rect Bounds, double Unit);

internal sealed class SourceSurface : FrameworkElement
{
    private BitmapSource? image;
    private SourceInk ink = new([]);
    private AnnotationSelection? selection;

    public SourceSurface() => IsHitTestVisible = false;

    public void Present(BitmapSource? nextImage, SourceInk nextInk, AnnotationSelection? nextSelection)
    {
        if (image == nextImage && ink == nextInk && selection == nextSelection)
            return;
        (image, ink, selection) = (nextImage, nextInk, nextSelection);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        Draw(dc, image, ink);
        if (selection is { } selected)
            BoardSelection.Draw(dc, selected.Bounds, selected.Unit);
    }

    internal static void Draw(DrawingContext dc, BitmapSource? image, SourceInk ink)
    {
        dc.PushClip(new RectangleGeometry(BoardSurface.Bounds));
        if (image is not null)
        {
            dc.DrawRectangle(Brushes.Black, null, BoardSurface.Bounds);
            var scale = Math.Min(BoardSurface.BoardWidth / image.PixelWidth, BoardSurface.BoardHeight / image.PixelHeight);
            var width = image.PixelWidth * scale;
            var height = image.PixelHeight * scale;
            dc.DrawImage(image, new Rect((BoardSurface.BoardWidth - width) / 2,
                (BoardSurface.BoardHeight - height) / 2, width, height));
        }
        BoardSurface.DrawItems(dc, ink.Marks);
        if (ink.Gesture is { } gesture)
            BoardSurface.DrawItems(dc, [gesture]);
        if (ink.Pointer is { } pointer)
            BoardSurface.DrawPointer(dc, pointer);
        dc.Pop();
    }
}
