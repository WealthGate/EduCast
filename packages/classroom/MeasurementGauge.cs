namespace EduCast.Classroom;

public enum GaugeMode { Outside, Inside, Depth }
public enum GaugeSample { None, Sheet, Rod, Block, Bore, Recess }

public sealed record MeasurementGauge(InstrumentKind Kind, GaugeMode Mode, int Opening, GaugeSample Sample,
    bool ShowReading = true, bool CloseUp = true)
{
    // All lengths are in hundredths of a millimetre, so scale carries and contact are exact.
    public const int VernierDivisions = 50;
    public const int ScrewPitch = 50;
    public bool IsCaliper => Kind == InstrumentKind.VernierCaliper;
    public int Range => IsCaliper ? 15000 : 2500;
    public int Resolution => IsCaliper ? 2 : 1;
    public int MainReading => IsCaliper ? Opening / 100 * 100 : Opening / ScrewPitch * ScrewPitch;
    public int FineReading => Opening - MainReading;
    public int FineDivision => FineReading / Resolution;
    public int SpecimenSize => Sample switch
    {
        GaugeSample.Sheet => 248,
        GaugeSample.Rod => 1236,
        GaugeSample.Block => 2364,
        GaugeSample.Bore => 2834,
        GaugeSample.Recess => 1846,
        _ => 0
    };
    public bool AtContact => Sample != GaugeSample.None && Opening == SpecimenSize;

    public static MeasurementGauge Create(InstrumentKind kind) =>
        new(kind, GaugeMode.Outside, kind == InstrumentKind.VernierCaliper ? 1542 : 738, GaugeSample.None);

    public MeasurementGauge Adjust(double hundredths)
    {
        if (!double.IsFinite(hundredths))
            return this;
        var lower = Mode == GaugeMode.Outside ? SpecimenSize : 0;
        var upper = Mode != GaugeMode.Outside && Sample != GaugeSample.None ? SpecimenSize : Range;
        var clamped = Math.Clamp(hundredths, lower, upper);
        var steps = Math.Round(clamped / Resolution, MidpointRounding.AwayFromZero);
        return this with { Opening = (int)steps * Resolution };
    }

    public MeasurementGauge UseSample(GaugeSample sample)
    {
        var next = this with { Sample = sample };
        if (sample == GaugeSample.None)
            return next;
        var clearance = IsCaliper ? 160 : 60;
        return next.Adjust(next.SpecimenSize + (Mode == GaugeMode.Outside ? clearance : -clearance));
    }

    public double VernierTick(int division) => Opening / 100d + division * (VernierDivisions - 1d) / VernierDivisions;

    public int ThimbleTick(int offset) =>
        ((FineDivision + offset) % ScrewPitch + ScrewPitch) % ScrewPitch;
}
