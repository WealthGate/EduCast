using System.Runtime.InteropServices;
using System.Windows.Threading;
using EduCast.Classroom;

namespace EduCast.Studio;

// Only a physical mouse click on the same existing target can turn an ink tap into a selection.
internal sealed class InkTap
{
    internal enum Overlay { Clock, Instrument }

    private readonly DispatcherTimer timer = new(DispatcherPriority.Input)
    {
        Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime())
    };
    private readonly Action<Ink> add;
    private Ink? pending;
    private object? target;
    private bool secondClick;

    internal InkTap(Action<Ink> add)
    {
        this.add = add;
        timer.Tick += (_, _) => Flush();
    }

    internal void Begin(int clickCount, object? hit, bool physicalMouse)
    {
        if (physicalMouse && clickCount == 2 && pending is not null && Equals(target, hit))
        {
            timer.Stop();
            secondClick = true;
            return;
        }
        Flush();
        target = physicalMouse && clickCount == 1 ? hit : null;
    }

    internal object? Collect(Ink ink)
    {
        var first = ink.Points[0];
        var tap = target is not null && ink.Points.All(point => InstrumentPose.Distance(point, first) < 3);
        if (secondClick && tap)
        {
            var hit = target;
            Clear();
            return hit;
        }
        if (!secondClick && tap)
        {
            pending = ink;
            timer.Start();
        }
        else
        {
            Flush();
            add(ink);
        }
        return null;
    }

    internal void Clear()
    {
        timer.Stop();
        pending = null;
        target = null;
        secondClick = false;
    }

    internal void Flush()
    {
        var stroke = pending;
        Clear();
        if (stroke is not null)
            add(stroke);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}
