namespace EduCast.Classroom;

public enum ClockView { Digital, Analog, Stopwatch, Countdown }

public sealed record ClockReading(ClockView View, TimeSpan Value)
{
    public string Text => Value.ToString(@"hh\:mm\:ss");
}

public sealed class ClassroomClock(TimeProvider time)
{
    private long? started;
    private TimeSpan elapsed;
    private TimeSpan duration = TimeSpan.FromMinutes(5);

    public ClockView View { get; private set; }
    public bool Running => started.HasValue;

    public ClockReading Read()
    {
        var value = View switch
        {
            ClockView.Digital or ClockView.Analog => time.GetLocalNow().TimeOfDay,
            ClockView.Stopwatch => Elapsed(),
            _ => duration - Elapsed()
        };
        if (value <= TimeSpan.Zero && View == ClockView.Countdown)
        {
            elapsed = duration;
            started = null;
            value = TimeSpan.Zero;
        }

        // Stopwatch displays completed seconds and countdown displays remaining seconds.
        var seconds = View == ClockView.Countdown ? Math.Ceiling(value.TotalSeconds) : Math.Floor(value.TotalSeconds);
        return new ClockReading(View, TimeSpan.FromSeconds(seconds));
    }

    public void Select(ClockView view, TimeSpan countdown)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(countdown, TimeSpan.FromSeconds(1));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(countdown, TimeSpan.FromHours(12));
        if (view == View && (View != ClockView.Countdown || duration == countdown))
            return;
        View = view;
        duration = countdown;
        Reset();
    }

    public void Toggle()
    {
        if (View is ClockView.Digital or ClockView.Analog)
            return;
        if (Running)
        {
            elapsed = Elapsed();
            started = null;
        }
        else if (View != ClockView.Countdown || elapsed < duration)
            started = time.GetTimestamp();
    }

    public void Reset()
    {
        elapsed = TimeSpan.Zero;
        started = null;
    }

    private TimeSpan Elapsed() => elapsed + (started is long start ? time.GetElapsedTime(start) : TimeSpan.Zero);
}
