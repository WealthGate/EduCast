using System.Windows.Threading;
using EduCast.Classroom;

namespace EduCast.Studio;

// Only a physical mouse click on the same existing target can turn an ink tap into a selection.
internal sealed class InkTap
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Input)
    {
        Interval = CanvasClick.Interval
    };
    private readonly Action<Ink> add;
    private Ink? pending;
    private CanvasTarget? target;
    private bool secondClick;

    internal InkTap(Action<Ink> add)
    {
        this.add = add;
        timer.Tick += (_, _) => Flush();
    }

    internal void Begin(int clickCount, CanvasTarget? hit, bool physicalMouse)
    {
        if (physicalMouse && clickCount == 2 && pending is not null && target == hit)
        {
            timer.Stop();
            secondClick = true;
            return;
        }
        Flush();
        target = physicalMouse && clickCount == 1 ? hit : null;
    }

    internal CanvasTarget? Collect(Ink ink, double unit = 1)
    {
        var first = ink.Points[0];
        var tap = target is not null && ink.Points.All(point => InstrumentPose.Distance(point, first) < 3 * unit);
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
}
