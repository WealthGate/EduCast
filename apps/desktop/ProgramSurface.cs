using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EduCast.Classroom;

namespace EduCast.Studio;

internal sealed record ProgramLayers(BitmapSource? CameraOne = null, BitmapSource? CameraTwo = null, ClockReading? Clock = null);
internal sealed record StudioFrame(ProgramFrame? Board, ProgramInk? LiveInk, ProgramLayers Layers);

internal sealed class ProgramSurface : Grid
{
    private readonly BoardSurface board = new();
    private readonly LayerSurface layers = new();
    private ProgramLayers requested = new();
    private bool black = true;

    public ProgramSurface()
    {
        Width = BoardSurface.BoardWidth;
        Height = BoardSurface.BoardHeight;
        IsHitTestVisible = false;
        Children.Add(board);
        Children.Add(layers);
    }

    public void Present(ProgramFrame? frame)
    {
        board.Present(frame);
        black = frame is null;
        if (black)
            board.PresentInk(null);
        layers.Present(black ? new ProgramLayers() : requested);
    }

    public void PresentInk(ProgramInk? ink) => board.PresentInk(black ? null : ink);

    public void PresentLayers(ProgramLayers frame)
    {
        requested = frame;
        if (!black)
            layers.Present(frame);
    }

    private sealed class LayerSurface : FrameworkElement
    {
        private ProgramLayers frame = new();
        private static readonly Brush Panel = FrozenBrush(Color.FromRgb(14, 23, 36));
        private static readonly Brush Border = FrozenBrush(Color.FromRgb(119, 223, 196));
        private static readonly Typeface Face = new("Segoe UI Semibold");

        public void Present(ProgramLayers next)
        {
            if (next == frame)
                return;
            frame = next;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Camera(dc, frame.CameraOne, 936);
            Camera(dc, frame.CameraTwo, 600);
            if (frame.Clock is { } clock)
                DrawClock(dc, clock);
        }

        private static void Camera(DrawingContext dc, BitmapSource? image, int x)
        {
            if (image is null)
                return;
            var rect = new Rect(x, 520, 320, 180);
            dc.DrawRoundedRectangle(Panel, new Pen(Border, 3), rect, 7, 7);
            dc.PushClip(new RectangleGeometry(rect, 7, 7));
            dc.DrawImage(image, rect);
            dc.Pop();
        }

        private static void DrawClock(DrawingContext dc, ClockReading clock)
        {
            if (clock.View == ClockView.Analog)
            {
                var center = new Point(1172, 98);
                dc.DrawEllipse(Panel, new Pen(Border, 2), center, 74, 74);
                for (var i = 0; i < 12; i++)
                    dc.DrawLine(new Pen(Border, 2), At(center, i / 12d, 61), At(center, i / 12d, 66));
                dc.DrawLine(new Pen(Brushes.White, 5), center, At(center, clock.Value.TotalHours / 12, 38));
                dc.DrawLine(new Pen(Brushes.White, 3), center, At(center, clock.Value.TotalMinutes / 60, 54));
                dc.DrawLine(new Pen(Border, 2), center, At(center, clock.Value.TotalSeconds / 60, 58));
                dc.DrawEllipse(Border, null, center, 4, 4);
                return;
            }

            var label = clock.View switch
            {
                ClockView.Stopwatch => "STOPWATCH",
                ClockView.Countdown => "COUNTDOWN",
                _ => "CLOCK"
            };
            dc.DrawRoundedRectangle(Panel, new Pen(Border, 2), new Rect(1030, 20, 226, 90), 10, 10);
            dc.DrawText(Format(label, 15, Border), new Point(1046, 31));
            dc.DrawText(Format(clock.Text, 38, Brushes.White), new Point(1046, 51));
        }

        private static Point At(Point center, double turns, double radius)
        {
            var angle = turns * Math.Tau;
            return center + new Vector(Math.Sin(angle) * radius, -Math.Cos(angle) * radius);
        }

        private static FormattedText Format(string value, double size, Brush color) => new(
            value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, color, 1);

        private static Brush FrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
