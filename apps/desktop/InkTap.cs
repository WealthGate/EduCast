using System.Runtime.InteropServices;
using System.Windows.Threading;
using EduCast.Classroom;

namespace EduCast.Studio;

// A tap over an existing object might be the first half of a double-click.
internal sealed class InkTap
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Input)
    {
        Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime())
    };
    private readonly Action<Ink> add;
    private Ink? pending;

    internal InkTap(Action<Ink> add)
    {
        this.add = add;
        timer.Tick += (_, _) => Flush();
    }

    internal void Collect(Ink ink, Func<BoardPoint, bool> overObject)
    {
        Flush();
        var first = ink.Points[0];
        if (ink.Points.All(point => InstrumentPose.Distance(point, first) < 3) && overObject(first))
        {
            pending = ink;
            timer.Start();
        }
        else
            add(ink);
    }

    internal void Clear()
    {
        timer.Stop();
        pending = null;
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
