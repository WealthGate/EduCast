using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EduCast.Classroom;
using EduCast.Studio;

internal static partial class Checks
{
    private static void GaugeArithmetic()
    {
        var caliper = MeasurementGauge.Create(InstrumentKind.VernierCaliper);
        Assert(caliper.Opening == 1542 && caliper.MainReading == 1500 && caliper.FineReading == 42);
        Assert(caliper.Adjust(1541).Opening == 1542 && caliper.Adjust(-1).Opening == 0);
        Assert(caliper.Adjust(caliper.Range + 99).Opening == 15000);
        for (var opening = 0; opening <= caliper.Range; opening += 2)
        {
            var gauge = caliper.Adjust(opening);
            var matchingMainMark = gauge.MainReading / 100 + gauge.FineDivision;
            Assert(Math.Abs(gauge.VernierTick(gauge.FineDivision) - matchingMainMark) < 1e-9);
            Assert(Math.Abs(gauge.VernierTick(50) - gauge.VernierTick(0) - 49) < 1e-9);
        }

        var micrometer = MeasurementGauge.Create(InstrumentKind.Micrometer);
        Assert(micrometer.Opening == 738 && micrometer.MainReading == 700 && micrometer.FineDivision == 38);
        Assert(micrometer.Adjust(799).MainReading == 750 && micrometer.Adjust(799).ThimbleTick(1) == 0);
        Assert(micrometer.Adjust(800).MainReading == 800 && micrometer.Adjust(800).ThimbleTick(-1) == 49);
        Assert(micrometer.Adjust(788).FineDivision == 38);
        Assert(micrometer.Adjust(2550).Opening == 2500 && micrometer.Adjust(-1).Opening == 0);

        var rod = micrometer.UseSample(GaugeSample.Rod);
        Assert(!rod.AtContact && rod.Opening > 1236 && rod.Adjust(0).AtContact && rod.Adjust(0).Opening == 1236);
        var bore = (caliper with { Mode = GaugeMode.Inside }).UseSample(GaugeSample.Bore);
        Assert(!bore.AtContact && bore.Opening < 2834 && bore.Adjust(5000).AtContact && bore.Adjust(5000).Opening == 2834);
        var recess = (caliper with { Mode = GaugeMode.Depth }).UseSample(GaugeSample.Recess);
        Assert(!recess.AtContact && recess.Adjust(5000).Opening == 1846 && recess.Adjust(5000).AtContact);
    }

    private static void GaugeDemonstration(TeacherWindow teacher, FrameworkElement student, byte[] paper)
    {
        void Check(string name, bool state)
        {
            var check = Find<CheckBox>(teacher, name);
            check.IsChecked = state;
            check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        void ExpectOpening(int hundredths)
        {
            var status = Find<TextBlock>(teacher, "InstrumentStatus").Text;
            Assert(status.StartsWith(GaugeDrawing.Millimetres(hundredths) + " mm ·", StringComparison.Ordinal), status);
        }

        Click(teacher, "MathToolsButton");
        var choice = Find<ComboBox>(teacher, "InstrumentChoice");
        choice.SelectedIndex = 8;
        ExpectOpening(1542);
        var overlay = Find<FrameworkElement>(teacher, "TeacherInstrument");
        Pump();
        Assert(Pixel(overlay, 550, 245)[3] > 0 && Pixel(student, 550, 245).SequenceEqual(paper),
            "private caliper was missing in preview or leaked to Program");

        // The ruler exercise previously calibrated the board to 40 units per centimetre.
        Assert(teacher.BeginMathDrag(new BoardPoint(320, 290)), "sliding carriage was not draggable");
        teacher.EndMathDrag(new BoardPoint(360, 290), snap: false);
        ExpectOpening(2542);
        Check("PublishInstrument", true);
        Pump();
        Assert(!Pixel(student, 550, 245).SequenceEqual(paper), "published caliper was missing");
        Assert(Pixel(student, 183, 259).SequenceEqual(paper), "caliper rotation control leaked");
        var match = Pixel(student, 584, 603);
        Assert(match[2] > 120 && match[2] > match[0], "caliper matching graduation was missing");

        Click(teacher, "FreezeButton");
        Check("GaugeShowReading", false);
        Pump();
        Assert(!Pixel(student, 584, 603).SequenceEqual(match), "frozen scale retained the answer highlight");
        Check("PublishInstrument", false);
        Pump();
        Assert(Pixel(student, 550, 245).SequenceEqual(paper), "unpublished caliper remained frozen");
        Click(teacher, "FreezeButton");

        var modes = Find<ComboBox>(teacher, "GaugeModeChoice");
        var samples = Find<ComboBox>(teacher, "GaugeSampleChoice");
        modes.SelectedIndex = 1;
        samples.SelectedIndex = 1;
        Assert(!Find<Button>(teacher, "GaugeWriteButton").IsEnabled, "readout could be saved before contact");
        Click(teacher, "GaugeContactButton");
        ExpectOpening(2834);
        Assert(Find<Button>(teacher, "GaugeWriteButton").IsEnabled);
        modes.SelectedIndex = 2;
        samples.SelectedIndex = 1;
        Click(teacher, "GaugeContactButton");
        ExpectOpening(1846);

        Click(teacher, "GaugeAlignButton");
        Assert(teacher.BeginMathDrag(new BoardPoint(300, 385)));
        teacher.EndMathDrag(new BoardPoint(340, 385), snap: false);
        ExpectOpening(1000);
        Assert(samples.SelectedIndex == 0, "aligning board endpoints did not remove the specimen");
        Click(teacher, "GaugeAlignButton");
        Assert(teacher.BeginMathDrag(new BoardPoint(300, 385)));
        teacher.EndMathDrag(new BoardPoint(1000, 385), snap: false);
        ExpectOpening(1000);
        Assert(Find<TextBlock>(teacher, "Status").Text.Contains("beyond", StringComparison.Ordinal));

        choice.SelectedIndex = 9;
        Assert(!modes.IsEnabled && modes.SelectedIndex == 0, "micrometer offered an unsupported mode");
        Assert(teacher.BeginMathDrag(new BoardPoint(398, 345)), "thimble was not draggable");
        teacher.EndMathDrag(new BoardPoint(398, 281), snap: false);
        ExpectOpening(788);
        Click(teacher, "GaugeContactButton");
        ExpectOpening(0);
        Assert(teacher.BeginMathDrag(new BoardPoint(396, 345)));
        teacher.EndMathDrag(new BoardPoint(396, 281), snap: false);
        ExpectOpening(50);

        Check("PublishInstrument", true);
        Pump();
        Assert(!Pixel(student, 410, 344).SequenceEqual(paper), "published micrometer was missing");
        Click(teacher, "RemoveInstrumentButton");
        Pump();
        Assert(Pixel(student, 410, 344).SequenceEqual(paper), "removed micrometer remained on Program");
    }
}
