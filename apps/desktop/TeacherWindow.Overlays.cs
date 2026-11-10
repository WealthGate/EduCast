using System.Collections.Immutable;
using System.Windows;
using EduCast.Classroom;

namespace EduCast.Studio;

public partial class TeacherWindow
{
    private sealed record ClockDrag(Point Start, Vector Original, Rect Bounds, bool Source);
    private ClockDrag? clockDrag;
    private Vector clockOffset;
    private Vector takenClockOffset;
    private ImmutableArray<PublishedInstrument> publishedInstruments = [];

    private (ClockReading Reading, Vector Offset)? TeacherClock => studioLayers.Clock is { } clock
        ? (clock, clockOffset) : null;

    internal bool ClockDragging(bool source) => clockDrag?.Source == source;

    internal bool OverClock(Point pointer) => VisibleClockBounds() is { } bounds && bounds.Contains(pointer);

    internal bool BeginClockDrag(Point pointer, bool source)
    {
        if (VisibleClockBounds() is not { } bounds || !bounds.Contains(pointer))
            return false;
        clockDrag = new ClockDrag(pointer, clockOffset, bounds, source);
        return true;
    }

    private Rect? VisibleClockBounds()
    {
        if (studioLayers.Clock is not { } clock)
            return null;
        var bounds = ProgramSurface.ClockBounds(clock);
        bounds.Offset(clockOffset);
        return bounds;
    }

    internal void RemoveClock()
    {
        clockDrag = null;
        if (studio is not null)
            studio.RemoveClock();
        ApplyStudioLayers(studioLayers with { Clock = null });
    }

    internal void MoveClock(Point pointer)
    {
        if (clockDrag is not { } drag)
            return;
        clockOffset = drag.Original + BoardSurface.ConstrainMove(drag.Bounds, pointer - drag.Start);
        RefreshTeacherOverlays();
        RefreshStudioLayers();
    }

    internal void FinishClockDrag() => clockDrag = null;

    internal void CancelClockDrag(bool source)
    {
        if (clockDrag is not { } drag || drag.Source != source)
            return;
        clockOffset = drag.Original;
        clockDrag = null;
        RefreshTeacherOverlays();
        RefreshStudioLayers();
    }

    private ImmutableArray<PublishedInstrument> CurrentInstruments()
    {
        var current = instruments.Where(item => item.Published is not null)
            .Select(item => new PublishedInstrument(item.Id, item.Source, item.Published!));
        if (!current.SequenceEqual(publishedInstruments))
            publishedInstruments = current.ToImmutableArray();
        return publishedInstruments;
    }

    private ImmutableArray<PublishedInstrument> FilterHeldInstruments(ImmutableArray<PublishedInstrument> held)
    {
        if (held.IsDefaultOrEmpty)
            return held;
        var current = instruments.Where(item => item.Published is not null).ToDictionary(item => item.Id);
        var filtered = held.Where(item => current.ContainsKey(item.Id))
            .Select(item => item with { Display = ConcealGaugeReading(item.Display, current[item.Id].Display)! });
        return filtered.SequenceEqual(held) ? held : filtered.ToImmutableArray();
    }
}
