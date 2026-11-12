using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EduCast.Classroom;

namespace EduCast.Studio;

internal static class ProgramImage
{
    public static void WritePng(ProgramFrame? frame, Stream destination) =>
        WritePng(new StudioFrame(frame, null, new ProgramLayers()), destination);

    public static void WritePng(StudioFrame frame, Stream destination)
    {
        var renderer = new ProgramSurface();
        var size = new Size(BoardSurface.BoardWidth, BoardSurface.BoardHeight);
        renderer.Present(frame.Board);
        var source = frame.Layers.Source;
        renderer.PresentLayers(frame.Layers with
        {
            Pointer = null,
            Source = source is null ? null : source with { Ink = source.Ink with { Gesture = null, Pointer = null } }
        });
        renderer.SetMotionTime(frame.MotionTime);
        renderer.Measure(size);
        renderer.Arrange(new Rect(size));
        var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(renderer);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(destination);
    }
}
