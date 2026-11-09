using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Threading;
using EduCast.Classroom;

namespace EduCast.Studio;

internal sealed class LiveInkCanvas : InkCanvas
{
    private const int PreviewPoints = 2048;
    private const int ProjectPoints = 30000;
    private readonly List<BoardPoint> samples = [];
    private Ink? template;
    private int? stylusId;
    private DispatcherOperation? refresh;
    private bool cancelling;

    internal Func<DrawingAttributes, Ink> NewStroke { private get; set; } = null!;
    internal event Action<Ink?>? PreviewChanged;

    public LiveInkCanvas()
    {
        AddHandler(Stylus.PreviewStylusDownEvent, new StylusDownEventHandler(PenDown), true);
        AddHandler(Stylus.PreviewStylusMoveEvent, new StylusEventHandler(PenMove), true);
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(MouseDownPreview), true);
        AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(MouseMovePreview), true);
        LostMouseCapture += (_, _) => ClearAfterCaptureLoss();
        LostStylusCapture += (_, _) => ClearAfterCaptureLoss();
    }

    private void PenDown(object sender, StylusDownEventArgs args)
    {
        if (EditingMode != InkCanvasEditingMode.Ink || template is not null)
            return;
        stylusId = args.StylusDevice.Id;
        BeginPreview();
        AddPreview(args.GetStylusPoints(this));
    }

    private void PenMove(object sender, StylusEventArgs args)
    {
        if (IsStylusCaptured && stylusId == args.StylusDevice.Id)
            AddPreview(args.GetStylusPoints(this));
    }

    private void MouseDownPreview(object sender, MouseButtonEventArgs args)
    {
        if (EditingMode != InkCanvasEditingMode.Ink || args.ChangedButton != MouseButton.Left ||
            args.StylusDevice is not null || template is not null)
            return;
        BeginPreview();
        var point = args.GetPosition(this);
        AddSample(ToBoardPoint(point.X, point.Y, .5f));
    }

    private void MouseMovePreview(object sender, MouseEventArgs args)
    {
        if (!IsMouseCaptured || stylusId is not null || args.StylusDevice is not null ||
            args.LeftButton != MouseButtonState.Pressed || template is null)
            return;
        var point = args.GetPosition(this);
        AddSample(ToBoardPoint(point.X, point.Y, .5f));
    }

    internal void BeginPreview() => template = NewStroke(DefaultDrawingAttributes);

    internal void AddPreview(StylusPointCollection points)
    {
        if (template is null)
            return;
        foreach (var point in points)
            AddSample(ToBoardPoint(point.X, point.Y, point.PressureFactor));
    }

    private void AddSample(BoardPoint point)
    {
        if (samples.Count > 0 && samples[^1] == point)
            return;
        if (samples.Count == PreviewPoints * 2)
        {
            for (var i = 1; i < PreviewPoints; i++)
                samples[i] = samples[i * 2];
            samples.RemoveRange(PreviewPoints, PreviewPoints);
        }
        samples.Add(point);
        refresh ??= Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            refresh = null;
            if (template is not null && samples.Count > 0)
                PreviewChanged?.Invoke(template with { Points = Sample(samples.Count, PreviewPoints, i => samples[i]) });
        }));
    }

    internal Ink? FinishStroke(Stroke stroke)
    {
        Strokes.Remove(stroke);
        if (cancelling)
            return null;

        var ink = template ?? NewStroke(stroke.DrawingAttributes);
        ClearPreview();
        var points = stroke.StylusPoints;
        return ink with
        {
            Points = Sample(points.Count, ProjectPoints, index =>
            {
                var point = points[index];
                return ToBoardPoint(point.X, point.Y, point.PressureFactor);
            })
        };
    }

    internal void CancelStroke()
    {
        if (template is null)
            return;
        ClearPreview();
        // Changing the editing mode discards the native in-progress stroke as well.
        cancelling = true;
        try
        {
            var mode = EditingMode;
            var invertedMode = EditingModeInverted;
            SetCurrentValue(EditingModeProperty, InkCanvasEditingMode.None);
            SetCurrentValue(EditingModeInvertedProperty, InkCanvasEditingMode.None);
            SetCurrentValue(EditingModeInvertedProperty, invertedMode);
            SetCurrentValue(EditingModeProperty, mode);
        }
        finally { cancelling = false; }
    }

    private void ClearAfterCaptureLoss()
    {
        var ending = template;
        if (ending is null)
            return;
        // InkCanvas can complete a stroke in its own capture-loss handler.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (ReferenceEquals(ending, template) && !IsMouseCaptured && !IsStylusCaptured)
                ClearPreview();
        }));
    }

    private void ClearPreview()
    {
        template = null;
        stylusId = null;
        samples.Clear();
        refresh?.Abort();
        refresh = null;
        PreviewChanged?.Invoke(null);
    }

    private static ImmutableArray<BoardPoint> Sample(int count, int limit, Func<int, BoardPoint> point)
    {
        if (count <= limit)
        {
            var all = ImmutableArray.CreateBuilder<BoardPoint>(count);
            for (var i = 0; i < count; i++)
                all.Add(point(i));
            return all.MoveToImmutable();
        }

        var sampled = ImmutableArray.CreateBuilder<BoardPoint>(limit);
        for (var i = 0; i < limit; i++)
            sampled.Add(point((int)((long)i * (count - 1) / (limit - 1))));
        return sampled.MoveToImmutable();
    }

    private static BoardPoint ToBoardPoint(double x, double y, float pressure) => new(
        Math.Clamp(x, 0, BoardSurface.BoardWidth), Math.Clamp(y, 0, BoardSurface.BoardHeight), pressure);
}
