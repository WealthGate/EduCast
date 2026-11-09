using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EduCast.Classroom;
using BoardItem = EduCast.Classroom.Content;
using ItemVisibility = EduCast.Classroom.Visibility;

namespace EduCast.Studio;

public partial class TeacherWindow
{
    private sealed record InstrumentOption(InstrumentKind Kind, string Label);
    private sealed record Calculation(string Source, string Result)
    {
        public override string ToString() => $"{Source} = {Result}";
    }

    private sealed class MathDrag(InstrumentDisplay original, BoardPoint start, InstrumentHandle? handle,
        GuideEdge? edge, bool calibration, Ink stroke)
    {
        public InstrumentDisplay Original { get; } = original;
        public BoardPoint Start { get; } = start;
        public InstrumentHandle? Handle { get; } = handle;
        public GuideEdge? Edge { get; } = edge;
        public bool Calibration { get; } = calibration;
        public Ink Stroke { get; } = stroke;
        public double StartBearing { get; } = InstrumentPose.Bearing(original.Pose.Origin, start);
        public double Sweep { get; set; }
        public double PreviousBearing { get; set; } = InstrumentPose.Bearing(original.Pose.Origin, start);
    }

    private InstrumentDisplay? instrumentDisplay;
    private InstrumentDisplay? publishedInstrument;
    private MathDrag? mathDrag;
    private Ink? instrumentTrace;
    private double unitsPerCentimetre = InstrumentPose.DefaultUnitsPerCentimetre;
    private double? calibrationLength;
    private readonly List<Calculation> calculationHistory = [];

    private void InitializeMathTools()
    {
        InstrumentChoice.ItemsSource = new InstrumentOption[]
        {
            new(InstrumentKind.Ruler, "Ruler"), new(InstrumentKind.Compass, "Compass"),
            new(InstrumentKind.Protractor180, "Protractor 180°"), new(InstrumentKind.Protractor360, "Protractor 360°"),
            new(InstrumentKind.SetSquare45, "Set square 45°"), new(InstrumentKind.SetSquare30, "Set square 30°/60°"),
            new(InstrumentKind.FrenchCurve, "French curve"), new(InstrumentKind.Calculator, "Calculator")
        };
        foreach (var value in new[] { "7", "8", "9", "÷", "4", "5", "6", "×", "1", "2", "3", "−",
            "0", ".", "+", "^", "(", ")", "⌫", "Clear" })
        {
            var key = new Button { Content = value, Padding = new Thickness(7, 4, 7, 4) };
            key.Click += (_, _) => InsertCalculatorKey(value);
            CalculatorKeys.Children.Add(key);
        }
    }

    private void OpenMathTools(object sender, RoutedEventArgs args)
    {
        InstrumentBar.Visibility = System.Windows.Visibility.Visible;
        if (InstrumentChoice.SelectedIndex < 0)
            InstrumentChoice.SelectedIndex = 0;
        else
            UseMathInstrument(sender, args);
    }

    private void ChangeInstrument(object sender, SelectionChangedEventArgs args)
    {
        if (!ready || InstrumentChoice.SelectedItem is not InstrumentOption selected)
            return;
        CancelGesture();
        ClearSelection();
        PublishInstrument.IsChecked = false;
        var pose = InstrumentPose.Create(selected.Kind, unitsPerCentimetre, RulerInches.IsChecked == true);
        instrumentDisplay = new InstrumentDisplay(pose);
        TraceInstrument.IsEnabled = pose.CanTrace;
        TraceInstrument.IsChecked = false;
        RulerControls.Visibility = pose.Kind == InstrumentKind.Ruler
            ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        CalculatorControls.Visibility = pose.Kind == InstrumentKind.Calculator
            ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ToggleCalculatorKeys(sender, args);
        InstrumentDrawButton.Visibility = pose.Kind == InstrumentKind.Calculator
            ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        InstrumentDrawButton.Content = pose.Kind switch
        {
            InstrumentKind.Compass => "Draw circle",
            InstrumentKind.Protractor180 or InstrumentKind.Protractor360 => "Draw angle",
            InstrumentKind.SetSquare45 or InstrumentKind.SetSquare30 => "Draw triangle",
            InstrumentKind.FrenchCurve => "Draw full curve",
            _ => "Draw ruler edge"
        };
        SetTool(Tool.Instrument);
        RefreshMathInstrument();
        Refresh();
    }

    private void UseMathInstrument(object sender, RoutedEventArgs args)
    {
        if (instrumentDisplay is null)
            return;
        CancelGesture();
        ClearSelection();
        SetTool(Tool.Instrument);
        Refresh();
    }

    private void RemoveMathInstrument(object sender, RoutedEventArgs args) => CloseMathTools();

    private void CloseMathTools()
    {
        if (instrumentDisplay is null)
            return;
        CancelGesture();
        instrumentDisplay = null;
        PublishInstrument.IsChecked = false;
        InstrumentChoice.SelectedIndex = -1;
        InstrumentBar.Visibility = System.Windows.Visibility.Collapsed;
        if (tool == Tool.Instrument)
            SetTool(Tool.Pen);
        RefreshMathInstrument();
    }

    private void ChangeTraceMode(object sender, RoutedEventArgs args)
    {
        UseMathInstrument(sender, args);
        RefreshMathInstrument();
    }

    private void ChangeInstrumentPublication(object sender, RoutedEventArgs args) => RefreshMathInstrument(publish: true);

    private void RefreshMathInstrument(bool publish = false)
    {
        TeacherInstrument.Present(instrumentDisplay, instrumentTrace, TraceInstrument.IsChecked == true);
        if (PublishInstrument.IsChecked != true || instrumentDisplay is null)
            publishedInstrument = null;
        else if (session.Live || publish)
            publishedInstrument = instrumentDisplay;
        RefreshStudioLayers();
        RefreshLiveInk();

        if (calibrationLength is not null || instrumentDisplay is not { Pose: var pose })
            return;
        InstrumentStatus.Text = pose.Kind switch
        {
            InstrumentKind.Ruler => $"{pose.Size / pose.UnitsPerMeasure:0.##} {pose.Unit} · {InstrumentPose.Normalize(pose.Rotation):0.#}°",
            InstrumentKind.Compass => $"Radius {pose.Size / pose.UnitsPerMeasure:0.##} {pose.Unit}",
            InstrumentKind.Protractor180 or InstrumentKind.Protractor360 => $"Angle {pose.Angle:0.#}°",
            InstrumentKind.SetSquare45 or InstrumentKind.SetSquare30 => $"Baseline {InstrumentPose.Normalize(pose.Rotation):0.#}°",
            InstrumentKind.FrenchCurve => $"Span {pose.Size:0} · rise {pose.CurveRise:0} board units",
            _ => "Enter an expression and select Evaluate"
        };
    }

    private void ChangeRulerUnits(object sender, RoutedEventArgs args)
    {
        if (instrumentDisplay is { Pose: var pose })
        {
            instrumentDisplay = instrumentDisplay with { Pose = pose with { Inches = RulerInches.IsChecked == true } };
            RefreshMathInstrument();
        }
    }

    private void CalibrateRuler(object sender, RoutedEventArgs args)
    {
        if (instrumentDisplay is not { Pose.Kind: InstrumentKind.Ruler } display)
            return;
        if ((!double.TryParse(ReferenceLength.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var reference) &&
            !double.TryParse(ReferenceLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out reference)) ||
            !double.IsFinite(reference) || reference <= 0)
        {
            Report("Enter a positive known reference length.");
            return;
        }
        UseMathInstrument(sender, args);
        calibrationLength = reference * (display.Pose.Inches ? 2.54 : 1);
        InstrumentStatus.Text = $"Drag between the reference endpoints for {reference:0.###} {display.Pose.Unit}. Esc cancels.";
    }

    private void ResetRulerScale(object sender, RoutedEventArgs args) =>
        SetCalibration(InstrumentPose.DefaultUnitsPerCentimetre);

    private void SetCalibration(double units)
    {
        unitsPerCentimetre = units;
        if (instrumentDisplay is { Pose: var pose })
            instrumentDisplay = instrumentDisplay with { Pose = pose with { UnitsPerCentimetre = units } };
        RefreshMathInstrument();
    }

    private Ink ConstructionInk(ItemVisibility? visibility = null) =>
        new(Guid.NewGuid(), visibility ?? newVisibility, visibility is null && newAnswer, InkColor, InkWidth, false, false, []);

    private static BoardPoint Pointer(Point point) => new(point.X, point.Y);

    internal bool BeginMathDrag(BoardPoint pointer)
    {
        if (instrumentDisplay is not { Pose: var pose } display)
            return false;
        if (calibrationLength is not null)
        {
            mathDrag = new MathDrag(display, pointer, null, null, true, ConstructionInk(ItemVisibility.TeacherOnly));
            return true;
        }

        var local = pose.ToLocal(pointer);
        if (TraceInstrument.IsChecked == true && pose.CanTrace)
        {
            var edges = pose.Edges();
            var edge = edges.IsEmpty ? (GuideEdge?)null :
                edges.MinBy(candidate => InstrumentPose.Distance(local, candidate.Closest(local)));
            var onPath = pose.Kind switch
            {
                InstrumentKind.Compass => Math.Abs(InstrumentPose.Distance(default, local) - pose.Size) < 24,
                InstrumentKind.FrenchCurve => InstrumentDrawing.Contains(pose, local),
                _ => edge is { } line && InstrumentPose.Distance(local, line.Closest(local)) < 23
            };
            if (onPath)
            {
                mathDrag = new MathDrag(display, pointer, null, edge, false, ConstructionInk());
                UpdateMathDrag(pointer, snap: false);
                return true;
            }
        }

        foreach (var (handle, position) in InstrumentDrawing.Handles(pose))
        {
            if (InstrumentPose.Distance(local, position) <= 15)
            {
                mathDrag = new MathDrag(display, pointer, handle, null, false, ConstructionInk());
                return true;
            }
        }
        if (!InstrumentDrawing.Contains(pose, local))
            return false;
        mathDrag = new MathDrag(display, pointer, InstrumentHandle.Move, null, false, ConstructionInk());
        return true;
    }

    internal void UpdateMathDrag(BoardPoint pointer, bool snap)
    {
        if (mathDrag is not { } drag)
            return;
        var pose = drag.Original.Pose;
        var local = pose.ToLocal(pointer);
        if (drag.Calibration)
            instrumentTrace = drag.Stroke with { Points = [Bound(drag.Start), Bound(pointer)] };
        else if (drag.Handle is { } handle)
        {
            var min = pose.Kind == InstrumentKind.Calculator ? 215 : pose.IsProtractor ? 90 : 40;
            pose = handle switch
            {
                InstrumentHandle.Move => pose with { Origin = Bound(new BoardPoint(
                    Snap(pose.Origin.X + pointer.X - drag.Start.X, snap ? 32 : 0),
                    Snap(pose.Origin.Y + pointer.Y - drag.Start.Y, snap ? 32 : 0))) },
                InstrumentHandle.Rotate => pose with { Rotation = InstrumentPose.Normalize(
                    Snap(pose.Rotation + InstrumentPose.Bearing(pose.Origin, pointer) - drag.StartBearing, snap ? 15 : 0)) },
                InstrumentHandle.Resize => pose with { Size = Math.Clamp(
                    pose.Kind == InstrumentKind.FrenchCurve ? local.X * 2 : local.X, min, 600) },
                InstrumentHandle.Angle => pose with { Angle = Snap(pose.ReadAngle(pointer), snap ? 15 : 1) },
                _ => pose with { CurveRise = Math.Clamp(-local.Y / Math.Pow(.68, 2), -260, 260) }
            };
            instrumentDisplay = drag.Original with { Pose = pose };
        }
        else if (drag.Edge is { } edge)
        {
            BoardPoint OnEdge(BoardPoint point)
            {
                var closest = edge.Closest(point);
                return snap && pose.Kind == InstrumentKind.Ruler
                    ? new BoardPoint(Math.Clamp(Snap(closest.X, pose.UnitsPerMeasure), 0, pose.Size), 0)
                    : closest;
            }
            instrumentTrace = drag.Stroke with
            {
                Points = [pose.ToBoard(OnEdge(pose.ToLocal(drag.Start))), pose.ToBoard(OnEdge(local))]
            };
        }
        else if (pose.Kind == InstrumentKind.FrenchCurve)
            instrumentTrace = drag.Stroke with { Points = pose.TraceCurve(pose.ToLocal(drag.Start).X, local.X) };
        else if (InstrumentPose.Distance(default, local) > 8)
        {
            var bearing = InstrumentPose.Bearing(pose.Origin, pointer);
            drag.Sweep = Math.Clamp(drag.Sweep + InstrumentPose.SweepStep(drag.PreviousBearing, bearing), -360, 360);
            drag.PreviousBearing = bearing;
            instrumentDisplay = drag.Original with { Pose = pose with { Rotation = drag.StartBearing + drag.Sweep } };
            var count = Math.Max(1, (int)Math.Ceiling(Math.Abs(drag.Sweep) / 2));
            var compass = pose with { Rotation = drag.StartBearing };
            instrumentTrace = drag.Stroke with
            {
                Points = Enumerable.Range(0, count + 1)
                    .Select(index => compass.ToBoard(InstrumentPose.Polar(drag.Sweep * index / count, pose.Size)))
                    .ToImmutableArray()
            };
        }
        RefreshMathInstrument();
    }

    internal void EndMathDrag(BoardPoint pointer, bool snap)
    {
        if (mathDrag is not { } drag)
            return;
        UpdateMathDrag(pointer, snap);
        var completed = instrumentTrace;
        var reference = calibrationLength;
        mathDrag = null;
        instrumentTrace = null;
        calibrationLength = null;
        RefreshMathInstrument();

        if (drag.Calibration)
        {
            var scale = InstrumentPose.Distance(Bound(drag.Start), Bound(pointer)) / reference.GetValueOrDefault();
            if (double.IsFinite(scale) && scale is >= 4 and <= 320)
            {
                SetCalibration(scale);
                Report("Ruler calibrated against the selected board reference.");
            }
            else
                Report("Reference is too short or too long for the ruler scale.");
            return;
        }
        if (drag.Handle is not null || completed is not { Points.Length: > 1 })
            return;
        var pose = drag.Original.Pose;
        if (pose.Kind == InstrumentKind.Compass)
        {
            if (Math.Abs(drag.Sweep) >= 1)
                AddItem(AsShape(completed, ShapeKind.Arc, pose.Origin, completed.Points[0], drag.Sweep));
        }
        else if (InstrumentPose.Distance(completed.Points[0], completed.Points[^1]) >= 2)
        {
            if (drag.Edge is not null)
                AddItems(LineWithOptionalLabel(completed, pose, completed.Points[0], completed.Points[^1]));
            else
                AddItem(completed);
        }
    }

    private void CancelMathDrag()
    {
        if (mathDrag is null && calibrationLength is null)
            return;
        if (mathDrag is { } drag)
            instrumentDisplay = drag.Original;
        mathDrag = null;
        instrumentTrace = null;
        calibrationLength = null;
        RefreshMathInstrument();
    }

    private void DrawFromInstrument(object sender, RoutedEventArgs args)
    {
        if (instrumentDisplay is not { Pose: var pose })
            return;
        var style = ConstructionInk();
        if (pose.Kind == InstrumentKind.Compass)
            AddItem(AsShape(style, ShapeKind.Circle, pose.Origin, pose.ToBoard(new BoardPoint(pose.Size, 0))));
        else if (pose.IsProtractor && pose.Angle > 0)
        {
            var first = pose.ToBoard(new BoardPoint(pose.Size, 0));
            var second = pose.ToBoard(InstrumentPose.Polar(-pose.Angle, pose.Size));
            var arc = pose.ToBoard(new BoardPoint(pose.Size * .35, 0));
            var label = pose.ToBoard(InstrumentPose.Polar(-pose.Angle / 2, pose.Size * .49));
            AddItems([
                AsShape(style, ShapeKind.Line, pose.Origin, first),
                AsShape(style with { Id = Guid.NewGuid() }, ShapeKind.Line, pose.Origin, second),
                AsShape(style with { Id = Guid.NewGuid() }, ShapeKind.Arc, pose.Origin, arc, -pose.Angle),
                MeasureLabel(style, label, $"{pose.Angle:0.#}°")
            ]);
        }
        else if (pose.Kind == InstrumentKind.Ruler)
            AddItems(LineWithOptionalLabel(style, pose, pose.Origin, pose.ToBoard(new BoardPoint(pose.Size, 0))));
        else if (pose.IsSetSquare)
            AddItems(pose.Edges().Select(edge => (BoardItem)AsShape(style with { Id = Guid.NewGuid() },
                ShapeKind.Line, pose.ToBoard(edge.Start), pose.ToBoard(edge.End))).ToImmutableArray());
        else if (pose.Kind == InstrumentKind.FrenchCurve)
            AddItem(style with { Points = pose.TraceCurve(-pose.Size / 2, pose.Size / 2) });
    }

    private ImmutableArray<BoardItem> LineWithOptionalLabel(Ink style, InstrumentPose pose, BoardPoint start, BoardPoint end)
    {
        var line = AsShape(style, ShapeKind.Line, start, end);
        if (pose.Kind != InstrumentKind.Ruler || RulerLabels.IsChecked != true)
            return [line];
        var length = InstrumentPose.Distance(start, end) / pose.UnitsPerMeasure;
        return [line, MeasureLabel(style, new BoardPoint((start.X + end.X) / 2 + 6, (start.Y + end.Y) / 2 - 24),
            $"{length:0.##} {pose.Unit}")];
    }

    private static Shape AsShape(Ink style, ShapeKind kind, BoardPoint start, BoardPoint end, double sweep = 0) =>
        new(style.Id, style.Visibility, style.IsAnswer, style.Argb, style.Width, kind, start, end, sweep);

    private static BoardText MeasureLabel(Ink style, BoardPoint origin, string text) =>
        new(Guid.NewGuid(), style.Visibility, style.IsAnswer, style.Argb, origin, 20, text);

    private static BoardPoint Bound(BoardPoint point) => PointOf(new Point(point.X, point.Y));
    private static double Snap(double value, double interval) => interval == 0 ? value : Math.Round(value / interval) * interval;

    private void CalculatorKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            CalculateOnBoard(sender, args);
            args.Handled = true;
        }
    }

    private void CalculateOnBoard(object sender, RoutedEventArgs args)
    {
        if (instrumentDisplay is not { Pose.Kind: InstrumentKind.Calculator } display)
            return;
        var expression = CalculatorEntry.Text.Trim();
        if (!MathParser.TryCalculate(expression, CalculatorDegrees.IsChecked == true, out var number, out var error))
        {
            Report(error);
            return;
        }
        var result = number.ToString("G12", CultureInfo.InvariantCulture);
        instrumentDisplay = display with { Expression = expression, Result = result };
        calculationHistory.Insert(0, new Calculation(expression, result));
        if (calculationHistory.Count > 12)
            calculationHistory.RemoveAt(12);
        CalculatorHistory.ItemsSource = calculationHistory.ToArray();
        RefreshMathInstrument();
        Report($"Result {result}.");
    }

    private void RestoreCalculation(object sender, SelectionChangedEventArgs args)
    {
        if (CalculatorHistory.SelectedItem is Calculation calculation)
            CalculatorEntry.Text = calculation.Source;
    }

    private void ToggleCalculatorKeys(object sender, RoutedEventArgs args) =>
        CalculatorKeys.Visibility = instrumentDisplay?.Pose.Kind == InstrumentKind.Calculator &&
            ShowCalculatorKeys.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    private void InsertCalculatorKey(string value)
    {
        if (value == "Clear")
        {
            CalculatorEntry.Clear();
            return;
        }

        var start = CalculatorEntry.SelectionStart;
        var length = CalculatorEntry.SelectionLength;
        var text = CalculatorEntry.Text;
        if (value == "⌫")
        {
            if (length == 0 && start > 0)
            {
                start--;
                length = 1;
            }
            value = "";
        }
        if (text.Length - length + value.Length > MathParser.MaxLength)
            return;
        CalculatorEntry.Text = text.Remove(start, length).Insert(start, value);
        CalculatorEntry.CaretIndex = start + value.Length;
        CalculatorEntry.Focus();
    }
}
