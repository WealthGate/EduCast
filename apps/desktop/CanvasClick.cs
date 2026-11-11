using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace EduCast.Studio;

internal enum CanvasTargetKind { Content, Clock, Instrument }
internal readonly record struct CanvasTarget(CanvasTargetKind Kind, Guid Id = default);

internal sealed class CanvasClick
{
    internal static TimeSpan Interval => TimeSpan.FromMilliseconds(GetDoubleClickTime());
    private readonly DispatcherTimer timer = new(DispatcherPriority.Input);
    private CanvasTarget? first;
    private Point start;
    private Action? pending;

    internal CanvasClick() => timer.Tick += (_, _) => FinishSingle();

    internal bool Begin(int count, CanvasTarget? target, Point position)
    {
        if (count == 2 && first.HasValue && first == target)
        {
            Clear();
            return true;
        }
        FinishSingle();
        first = count == 1 ? target : null;
        start = position;
        if (first.HasValue)
        {
            timer.Interval = Interval;
            timer.Start();
        }
        return false;
    }

    internal void Move(Point position, double unit)
    {
        if (pending is null && first.HasValue && IsDrag(start, position, unit))
            Clear();
    }

    internal static bool IsDrag(Point start, Point end, double unit) => (end - start).Length >= 3 * unit;

    // A placement or arc completion would otherwise replace the target of the second click.
    internal void Single(Action action)
    {
        if (first.HasValue)
            pending = action;
        else
            action();
    }

    internal void Clear()
    {
        timer.Stop();
        first = null;
        pending = null;
    }

    private void FinishSingle()
    {
        var action = pending;
        Clear();
        action?.Invoke();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}
