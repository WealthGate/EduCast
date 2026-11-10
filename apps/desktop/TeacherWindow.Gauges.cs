using System.Windows;
using System.Windows.Controls;
using EduCast.Classroom;

namespace EduCast.Studio;

public partial class TeacherWindow
{
    private sealed record GaugeModeOption(GaugeMode Mode, string Label);
    private sealed record GaugeSampleOption(GaugeSample Sample, string Label);

    private static readonly GaugeModeOption[] GaugeModes =
    [
        new(GaugeMode.Outside, "Outside"), new(GaugeMode.Inside, "Inside"), new(GaugeMode.Depth, "Depth")
    ];

    private bool alignGauge;
    private bool settingGaugeControls;

    private void ConfigureGaugeControls()
    {
        var gauge = instrumentDisplay?.Gauge;
        GaugeControls.Visibility = gauge is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        if (gauge is null)
            return;

        settingGaugeControls = true;
        var modes = gauge.IsCaliper ? GaugeModes : GaugeModes[..1];
        GaugeModeChoice.ItemsSource = modes;
        GaugeModeChoice.SelectedItem = modes.Single(option => option.Mode == gauge.Mode);
        GaugeModeChoice.IsEnabled = gauge.IsCaliper;

        GaugeSampleOption[] samples = gauge.Mode switch
        {
            GaugeMode.Inside => [new(GaugeSample.None, "No specimen"), new(GaugeSample.Bore, "Bore")],
            GaugeMode.Depth => [new(GaugeSample.None, "No specimen"), new(GaugeSample.Recess, "Recess")],
            _ => [new(GaugeSample.None, "No specimen"), new(GaugeSample.Sheet, "Thin sheet"),
                new(GaugeSample.Rod, "Round rod"), new(GaugeSample.Block, "Gauge block")]
        };
        GaugeSampleChoice.ItemsSource = samples;
        GaugeSampleChoice.SelectedItem = samples.Single(option => option.Sample == gauge.Sample);
        GaugeCloseUp.IsChecked = gauge.CloseUp;
        GaugeShowReading.IsChecked = gauge.ShowReading;
        settingGaugeControls = false;
    }

    private void RefreshGaugeStatus(MeasurementGauge gauge)
    {
        GaugeContactButton.Content = gauge.Sample == GaugeSample.None ? "Close to zero"
            : gauge.Mode == GaugeMode.Outside ? "Close to specimen" : "Advance to contact";
        GaugeWriteButton.IsEnabled = gauge.Sample == GaugeSample.None || gauge.AtContact;
        var contact = gauge.Sample == GaugeSample.None ? "" : gauge.AtContact ? " · At contact" : " · Adjust to contact";
        var action = gauge.IsCaliper ? "Slide the carriage" : "Drag the thimble up/down to turn";
        InstrumentStatus.Text = alignGauge
            ? "Drag between the two points to measure. Esc cancels."
            : $"{GaugeDrawing.Millimetres(gauge.Opening)} mm · {GaugeDrawing.Millimetres(gauge.Resolution)} mm/div · {action}{contact}";
    }

    private void OpenGauge(InstrumentKind kind, object sender, RoutedEventArgs args)
    {
        InstrumentBar.Visibility = System.Windows.Visibility.Visible;
        SetMathSurface(source: false);
        InstrumentChoice.SelectedItem = InstrumentChoice.Items.OfType<InstrumentOption>().Single(item => item.Kind == kind);
        AddInstrument(kind);
    }

    private void ChangeGaugeMode(object sender, SelectionChangedEventArgs args)
    {
        if (settingGaugeControls || GaugeModeChoice.SelectedItem is not GaugeModeOption option)
            return;
        CancelMathDrag();
        if (instrumentDisplay is not { Gauge: { } gauge, Pose: var pose } display || option.Mode == gauge.Mode)
            return;
        var oldDatum = GaugeDrawing.Datum(pose, gauge.Mode);
        var newDatum = GaugeDrawing.Datum(pose, option.Mode);
        var origin = pose.ToBoard(new BoardPoint(newDatum.X - oldDatum.X, newDatum.Y - oldDatum.Y));
        instrumentDisplay = display with
        {
            Pose = pose with { Origin = origin },
            Gauge = gauge with { Mode = option.Mode, Sample = GaugeSample.None }
        };
        ConfigureGaugeControls();
        RefreshMathInstrument();
    }

    private void ChangeGaugeSample(object sender, SelectionChangedEventArgs args)
    {
        if (settingGaugeControls || GaugeSampleChoice.SelectedItem is not GaugeSampleOption option)
            return;
        CancelMathDrag();
        if (instrumentDisplay is not { Gauge: { } gauge } display)
            return;
        instrumentDisplay = display with { Gauge = gauge.UseSample(option.Sample) };
        RefreshMathInstrument();
    }

    private void AlignGauge(object sender, RoutedEventArgs args)
    {
        if (instrumentDisplay?.Gauge is null)
            return;
        UseMathInstrument(sender, args);
        alignGauge = true;
        RefreshMathInstrument();
    }

    private void UpdateGaugeDrag(MathDrag drag, BoardPoint pointer, bool fine)
    {
        var display = drag.Original;
        var pose = display.Pose;
        var gauge = display.Gauge!;
        if (drag.Handle == InstrumentHandle.GaugeAlign)
        {
            var start = Bound(drag.Start);
            var end = Bound(pointer);
            var distance = InstrumentPose.Distance(start, end) * 100 / GaugeDrawing.Unit(pose);
            if (distance > gauge.Range + 1e-8)
            {
                instrumentDisplay = display;
                return;
            }

            var rotation = distance > 0 ? InstrumentPose.Bearing(start, end) : pose.Rotation;
            instrumentDisplay = display with
            {
                Pose = pose with { Origin = start, Rotation = rotation },
                Gauge = gauge.UseSample(GaugeSample.None).Adjust(distance)
            };
            return;
        }

        var local = pose.ToLocal(pointer);
        var startLocal = pose.ToLocal(drag.Start);
        // A 64-unit drag rotates the thimble through one complete revolution.
        var change = gauge.IsCaliper ? (local.X - startLocal.X) * 100 / GaugeDrawing.Unit(pose)
            : (startLocal.Y - local.Y) * MeasurementGauge.ScrewPitch / 64;
        instrumentDisplay = display with { Gauge = gauge.Adjust(gauge.Opening + change / (fine ? 10 : 1)) };
    }

    private void FinishGaugeAlignment(MathDrag drag, BoardPoint pointer)
    {
        UpdateGaugeDrag(drag, pointer, fine: false);
        alignGauge = false;
        mathDrag = null;
        if (ReferenceEquals(instrumentDisplay, drag.Original))
            Report($"Those points are beyond the instrument’s {GaugeDrawing.Millimetres(drag.Original.Gauge!.Range)} mm range.");
        else if (instrumentDisplay?.Gauge is { } aligned)
        {
            ConfigureGaugeControls();
            Report($"Measured {GaugeDrawing.Millimetres(aligned.Opening)} mm between the selected points.");
        }
        RefreshMathInstrument();
    }

    private void NudgeGauge(object sender, RoutedEventArgs args)
    {
        CancelMathDrag();
        if (instrumentDisplay?.Gauge is { } gauge && sender is Button button)
            SetGaugeOpening(gauge.Opening + (Equals(button.Tag, "1") ? gauge.Resolution : -gauge.Resolution));
    }

    private void GaugeContact(object sender, RoutedEventArgs args)
    {
        CancelMathDrag();
        if (instrumentDisplay?.Gauge is { } gauge)
            SetGaugeOpening(gauge.SpecimenSize);
    }

    private void SetGaugeOpening(double opening)
    {
        if (instrumentDisplay is not { Gauge: { } gauge } display)
            return;
        instrumentDisplay = display with { Gauge = gauge.Adjust(opening) };
        RefreshMathInstrument();
    }

    private bool TryGaugeWheel(BoardPoint pointer, int delta, bool coarse)
    {
        if (delta == 0 || mathDrag is not null || alignGauge ||
            instrumentDisplay is not { Gauge: { } gauge, Pose: var pose } ||
            !GaugeDrawing.OverAdjuster(pose, gauge, pose.ToLocal(pointer)))
            return false;

        var step = coarse ? (gauge.IsCaliper ? 100 : MeasurementGauge.ScrewPitch) : gauge.Resolution;
        SetGaugeOpening(gauge.Opening + Math.Sign(delta) * Math.Max(1, Math.Abs(delta) / 120) * step);
        return true;
    }

    private void ChangeGaugeReadout(object sender, RoutedEventArgs args)
    {
        CancelMathDrag();
        if (instrumentDisplay is not { Gauge: { } gauge } display)
            return;
        instrumentDisplay = display with
        {
            Gauge = gauge with { ShowReading = GaugeShowReading.IsChecked == true, CloseUp = GaugeCloseUp.IsChecked == true }
        };
        RefreshMathInstrument();
    }

    private static InstrumentDisplay? ConcealGaugeReading(InstrumentDisplay? held, InstrumentDisplay? live) =>
        held?.Gauge is { ShowReading: true } gauge && live?.Gauge is { ShowReading: false }
            ? held with { Gauge = gauge with { ShowReading = false } }
            : held;

    private void WriteGaugeReading(object sender, RoutedEventArgs args)
    {
        CancelMathDrag();
        if (instrumentDisplay?.Gauge is not { } gauge || gauge.Sample != GaugeSample.None && !gauge.AtContact)
            return;
        var text = GaugeDrawing.Reading(gauge);
        if (mathOnSource && sourceWindow is { } source)
            source.PlaceReading(text);
        else
            BeginPlacement(origin => new BoardText(Guid.NewGuid(), newVisibility, newAnswer, InkColor, origin, 26, text));
    }
}
