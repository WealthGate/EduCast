using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EduCast.Classroom;

namespace EduCast.Studio;

internal sealed class CanvasEraser(
    FrameworkElement input, CanvasFeedback feedback, ComboBox sizes,
    Func<double> scale, Func<ImmutableArray<Content>> read,
    Action preview, Action<ImmutableArray<Content>> commit)
{
    private static readonly double[] Diameters = [12, 24, 40, 64];
    private Point? previous;
    private Point? start;
    private StylusDevice? stylus;
    private Point? pointer;
    private bool active;

    internal ImmutableArray<Content>? Draft { get; private set; }
    private double Diameter => Diameters[sizes.SelectedIndex] * scale();

    internal void Attach()
    {
        sizes.ItemsSource = new[] { "Small", "Medium", "Large", "Extra large" };
        sizes.SelectedIndex = 1;
        sizes.SelectionChanged += (_, _) =>
        {
            if (active)
                feedback.ShowEraser(pointer, Diameter, scale(), Draft.HasValue);
        };
        input.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (!active || args.StylusDevice is not null)
                return;
            args.Handled = true;
            if (input.CaptureMouse())
                Begin(args.GetPosition(input));
        };
        input.PreviewMouseMove += (_, args) =>
        {
            if (active && args.StylusDevice is null && stylus is null)
                Follow(args.GetPosition(input));
        };
        input.PreviewMouseLeftButtonUp += (_, args) =>
        {
            if (!active || args.StylusDevice is not null || !Draft.HasValue)
                return;
            args.Handled = true;
            Finish(args.GetPosition(input));
            input.ReleaseMouseCapture();
        };
        input.LostMouseCapture += (_, _) =>
        {
            if (stylus is null && Draft.HasValue && !input.IsMouseCaptured)
                Cancel();
        };
        input.MouseLeave += (_, _) => HideWhenIdle();
        input.PreviewStylusDown += (_, args) =>
        {
            if (!active)
                return;
            args.Handled = true;
            if (!Draft.HasValue && args.StylusDevice.Capture(input))
            {
                stylus = args.StylusDevice;
                Begin(args.GetPosition(input));
            }
        };
        input.PreviewStylusMove += (_, args) =>
        {
            if (stylus != args.StylusDevice)
                return;
            args.Handled = true;
            foreach (var sample in args.GetStylusPoints(input))
                Follow(new Point(sample.X, sample.Y));
        };
        input.PreviewStylusInAirMove += (_, args) =>
        {
            if (active && !Draft.HasValue)
                Follow(args.GetPosition(input));
        };
        input.PreviewStylusUp += (_, args) =>
        {
            if (stylus != args.StylusDevice)
                return;
            args.Handled = true;
            Finish(args.GetPosition(input));
            args.StylusDevice.Capture(null);
        };
        input.LostStylusCapture += (_, args) =>
        {
            if (stylus == args.StylusDevice && !input.IsStylusCaptured)
                Cancel();
        };
        input.StylusLeave += (_, _) => HideWhenIdle();
        input.StylusOutOfRange += (_, _) => HideWhenIdle();
    }

    internal void Activate(bool enabled)
    {
        if (active && !enabled)
            Cancel();
        active = enabled;
        input.ForceCursor = enabled;
        input.Cursor = enabled ? Cursors.None : null;
    }

    internal void Cancel()
    {
        var drawing = Draft.HasValue;
        var pen = stylus;
        (Draft, start, previous, stylus, pointer) = (null, null, null, null, null);
        if (active)
            feedback.ShowEraser(null, Diameter, scale());
        if (!drawing)
            return;
        if (pen?.Captured == input)
            pen.Capture(null);
        if (input.IsMouseCaptured)
            input.ReleaseMouseCapture();
        preview();
    }

    private void Begin(Point point)
    {
        Draft = read();
        start = point;
        previous = null;
        Follow(point);
    }

    private void Follow(Point point)
    {
        pointer = point;
        feedback.ShowEraser(point, Diameter, scale(), Draft.HasValue);
        if (Draft is not { } items || previous == point ||
            previous is null && start is { } origin && !CanvasClick.IsDrag(origin, point, scale()))
            return;
        var next = BoardSurface.Erase(items, previous ?? start ?? point, point, Diameter);
        previous = point;
        if (next != items)
        {
            Draft = next;
            preview();
        }
    }

    private void Finish(Point point)
    {
        Follow(point);
        var next = Draft!.Value;
        (Draft, start, previous, stylus) = (null, null, null, null);
        feedback.ShowEraser(BoardSurface.Bounds.Contains(point) ? point : null, Diameter, scale());
        commit(next);
    }

    private void HideWhenIdle()
    {
        if (active && !Draft.HasValue)
        {
            pointer = null;
            feedback.ShowEraser(null, Diameter, scale());
        }
    }
}
