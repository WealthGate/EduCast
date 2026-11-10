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
    private sealed class PlacedInstrument(InstrumentDisplay display, bool source, string label)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Label { get; } = label;
        public bool Source { get; } = source;
        public InstrumentDisplay Display { get; set; } = display;
        public InstrumentDisplay? Published { get; set; }
        public bool Trace { get; set; }
        public bool LabelLength { get; set; }
    }

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
        public ArcGesture? Arc { get; } = handle is null && !calibration && original.Pose.Kind == InstrumentKind.Compass
            ? new ArcGesture(original.Pose.Origin, original.Pose.ToBoard(
                InstrumentPose.Polar(InstrumentPose.Bearing(original.Pose.Origin, start) - original.Pose.Rotation, original.Pose.Size)))
            : null;
    }

    private readonly List<PlacedInstrument> instruments = [];
    private PlacedInstrument? activeInstrument;
    private bool mathOnSource;
    private bool changingInstrument;
    private InstrumentDisplay? instrumentDisplay
    {
        get => activeInstrument?.Display;
        set => activeInstrument!.Display = value!;
    }

    private MathDrag? mathDrag;
    private BoardItem? instrumentTrace;
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
            new(InstrumentKind.FrenchCurve, "French curve"), new(InstrumentKind.Calculator, "Calculator"),
            new(InstrumentKind.VernierCaliper, "Vernier caliper"), new(InstrumentKind.Micrometer, "Micrometer screw gauge")
        };
        InstrumentChoice.SelectedIndex = 0;
        foreach (var value in new[] { "7", "8", "9", "÷", "4", "5", "6", "×", "1", "2", "3", "−",
            "0", ".", "+", "^", "(", ")", "⌫", "Clear" })
        {
            var key = new Button { Content = value, Padding = new Thickness(7, 4, 7, 4) };
            key.Click += (_, _) => InsertCalculatorKey(value);
            CalculatorKeys.Children.Add(key);
        }
    }

    private void OpenMathTools(object sender, RoutedEventArgs args) => OpenInstruments(source: false);

    internal void OpenInstruments(bool source)
    {
        SetMathSurface(source);
        InstrumentBar.Visibility = System.Windows.Visibility.Visible;
        if (activeInstrument is null && InstrumentChoice.SelectedItem is InstrumentOption selected)
            AddInstrument(selected.Kind);
        else
            UseMathInstrument(this, new RoutedEventArgs());
    }

    internal void SetMathSurface(bool source)
    {
        if (mathOnSource == source)
            return;
        CancelMathDrag();
        mathOnSource = source;
        if (source)
        {
            BoardInstrumentHost.Children.Clear();
            sourceWindow?.HostInstruments(InstrumentBar);
        }
        else
        {
            sourceWindow?.HostInstruments(null);
            if (InstrumentBar.Parent is System.Windows.Controls.Panel parent)
                parent.Children.Remove(InstrumentBar);
            BoardInstrumentHost.Children.Add(InstrumentBar);
        }
        SelectInstrument(instruments.LastOrDefault(item => item.Source == source));
        RefreshMathInstrument();
    }

    private void PlaceInstrument(object sender, RoutedEventArgs args)
    {
        if (InstrumentChoice.SelectedItem is InstrumentOption selected)
            AddInstrument(selected.Kind);
    }

    private void AddInstrument(InstrumentKind kind)
    {
        CancelMathDrag();
        var offset = instruments.Count(item => item.Source == mathOnSource) % 7 * 32;
        var pose = InstrumentPose.Create(kind, unitsPerCentimetre);
        pose = pose with { Origin = new BoardPoint(pose.Origin.X + offset, pose.Origin.Y + offset) };
        var label = InstrumentChoice.Items.OfType<InstrumentOption>().Single(option => option.Kind == kind).Label;
        var item = new PlacedInstrument(new InstrumentDisplay(pose,
            Gauge: pose.IsGauge ? MeasurementGauge.Create(pose.Kind) : null), mathOnSource, label);
        instruments.Add(item);
        SelectInstrument(item);
        UseMathInstrument(this, new RoutedEventArgs());
        RefreshMathInstrument();
    }

    private void ChoosePlacedInstrument(object sender, SelectionChangedEventArgs args)
    {
        if (changingInstrument || PlacedInstruments.SelectedItem is not PlacedInstrument item)
            return;
        CancelMathDrag();
        SelectInstrument(item);
        UseMathInstrument(sender, args);
        RefreshMathInstrument();
    }

    private void SelectInstrument(PlacedInstrument? item)
    {
        activeInstrument = item;
        changingInstrument = true;
        PlacedInstruments.ItemsSource = instruments.Where(candidate => candidate.Source == mathOnSource).ToArray();
        PlacedInstruments.SelectedItem = item;
        changingInstrument = false;
        var pose = item?.Display.Pose;
        var shown = System.Windows.Visibility.Visible;
        var hidden = System.Windows.Visibility.Collapsed;
        InstrumentActions.Visibility = pose is null ? hidden : shown;
        InstrumentOptions.Visibility = InstrumentActions.Visibility;
        ConfigureGaugeControls();
        TraceInstrument.IsEnabled = pose?.CanTrace == true;
        TraceInstrument.IsChecked = item?.Trace == true;
        PublishInstrument.IsChecked = item?.Published is not null;
        RulerInches.IsChecked = pose?.Inches == true;
        RulerLabels.IsChecked = item?.LabelLength == true;
        RulerControls.Visibility = pose?.Kind == InstrumentKind.Ruler ? shown : hidden;
        CalculatorControls.Visibility = pose?.Kind == InstrumentKind.Calculator ? shown : hidden;
        ToggleCalculatorKeys(this, new RoutedEventArgs());
        if (pose is null)
            return;
        InstrumentDrawButton.Visibility = pose.Kind == InstrumentKind.Calculator || pose.IsGauge ? hidden : shown;
        InstrumentDrawButton.Content = pose.Kind switch
        {
            InstrumentKind.Compass => "Draw circle",
            InstrumentKind.Protractor180 or InstrumentKind.Protractor360 => "Draw angle",
            InstrumentKind.SetSquare45 or InstrumentKind.SetSquare30 => "Draw triangle",
            InstrumentKind.FrenchCurve => "Draw full curve",
            _ => "Draw ruler edge"
        };
    }

    private void UseMathInstrument(object sender, RoutedEventArgs args)
    {
        if (instrumentDisplay is null)
            return;
        if (mathOnSource)
            sourceWindow?.UseInstrument();
        else
        {
            CancelGesture();
            ClearSelection();
            SetTool(Tool.Instrument);
            Refresh();
        }
    }

    private void RemoveMathInstrument(object sender, RoutedEventArgs args)
    {
        CancelMathDrag();
        if (activeInstrument is not null)
            instruments.Remove(activeInstrument);
        SelectInstrument(instruments.LastOrDefault(item => item.Source == mathOnSource));
        if (activeInstrument is null && !mathOnSource && tool == Tool.Instrument)
            SetTool(Tool.Select);
        RefreshMathInstrument();
    }

    private void CloseMathTools()
    {
        CancelMathDrag();
        instruments.RemoveAll(item => !item.Source);
        SelectInstrument(instruments.LastOrDefault(item => item.Source == mathOnSource));
        if (!mathOnSource)
        {
            InstrumentBar.Visibility = System.Windows.Visibility.Collapsed;
            if (tool == Tool.Instrument)
                SetTool(Tool.Select);
        }
        RefreshMathInstrument();
    }

    internal void ClearSourceInstruments()
    {
        if (mathOnSource)
            CancelMathDrag();
        instruments.RemoveAll(item => item.Source);
        if (mathOnSource)
            SelectInstrument(null);
        RefreshMathInstrument();
    }

    private void ChangeTraceMode(object sender, RoutedEventArgs args)
    {
        if (activeInstrument is null)
            return;
        activeInstrument.Trace = TraceInstrument.IsChecked == true;
        UseMathInstrument(sender, args);
        RefreshMathInstrument();
    }

    private void ChangeRulerLabels(object sender, RoutedEventArgs args)
    {
        if (activeInstrument is not null)
            activeInstrument.LabelLength = RulerLabels.IsChecked == true;
    }

    private void ChangeInstrumentPublication(object sender, RoutedEventArgs args)
    {
        if (activeInstrument is not null)
            activeInstrument.Published = PublishInstrument.IsChecked == true ? activeInstrument.Display : null;
        RefreshMathInstrument();
    }

    private void RefreshMathInstrument(bool publish = false)
    {
        foreach (var item in instruments.Where(item => item.Published is not null))
            item.Published = session.Live || publish ? item.Display : ConcealGaugeReading(item.Published, item.Display);

        RefreshTeacherOverlays();
        RefreshStudioLayers();
        RefreshLiveInk();
        RefreshProgramBoard();

        if (calibrationLength is not null || instrumentDisplay is not { Pose: var pose })
            return;
        if (instrumentDisplay.Gauge is { } gauge)
        {
            RefreshGaugeStatus(gauge);
            return;
        }
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

    private void RefreshTeacherOverlays()
    {
        TeacherInstrument.Present(instruments.Where(item => !item.Source).Select(item => item.Display).ToArray(),
            !mathOnSource ? instrumentDisplay : null, !mathOnSource ? instrumentTrace : null,
            !mathOnSource && activeInstrument?.Trace == true, TeacherClock);
        sourceWindow?.PresentInstruments(instruments.Where(item => item.Source).Select(item => item.Display).ToArray(),
            mathOnSource ? instrumentDisplay : null, mathOnSource ? instrumentTrace : null,
            mathOnSource && activeInstrument?.Trace == true, TeacherClock);
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
        foreach (var item in instruments)
            item.Display = item.Display with { Pose = item.Display.Pose with { UnitsPerCentimetre = units } };
        RefreshMathInstrument();
    }

    private Ink ConstructionInk(ItemVisibility? visibility = null) =>
        new(Guid.NewGuid(), visibility ?? (mathOnSource ? ItemVisibility.Both : newVisibility),
            !mathOnSource && visibility is null && newAnswer,
            mathOnSource && sourceWindow is { } source ? source.AnnotationColor : InkColor,
            mathOnSource && sourceWindow is { } window ? window.AnnotationWidth : InkWidth, false, false, []);

    private static BoardPoint Pointer(Point point) => new(point.X, point.Y);

    internal bool BeginMathDrag(BoardPoint pointer, bool source = false, bool selectOnly = false)
    {
        if (mathOnSource != source)
            return false;
        if (selectOnly || !alignGauge && calibrationLength is null)
        {
            var hit = instruments.Where(item => item.Source == source && item != activeInstrument)
                .Reverse().FirstOrDefault(item => OverInstrument(item.Display, pointer));
            if (activeInstrument is { } current && OverInstrument(current.Display, pointer))
                hit = current;
            if (hit is not null && hit != activeInstrument)
                SelectInstrument(hit);
            if (selectOnly)
            {
                if (hit is null)
                    return false;
                mathDrag = new MathDrag(hit.Display, pointer, InstrumentHandle.Move, null, false, ConstructionInk());
                RefreshMathInstrument();
                return true;
            }
        }
        if (instrumentDisplay is not { Pose: var pose } display)
            return false;
        if (display.Gauge is { } gauge)
        {
            var handle = alignGauge ? InstrumentHandle.GaugeAlign : GaugeDrawing.Hit(pose, gauge, pose.ToLocal(pointer));
            if (handle is null)
                return false;
            mathDrag = new MathDrag(display, pointer, handle, null, false, ConstructionInk());
            return true;
        }
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
        if (pose.Kind == InstrumentKind.Compass &&
            InstrumentPose.Distance(local, new BoardPoint(pose.Size, 0)) <= 13)
        {
            mathDrag = new MathDrag(display, pointer, InstrumentHandle.CompassTip, null, false, ConstructionInk());
            return true;
        }
        if (!InstrumentDrawing.Contains(pose, local))
            return false;
        mathDrag = new MathDrag(display, pointer, InstrumentHandle.Move, null, false, ConstructionInk());
        return true;
    }

    internal bool OverInstrument(BoardPoint pointer, bool source) =>
        instruments.Any(item => item.Source == source && OverInstrument(item.Display, pointer));

    private static bool OverInstrument(InstrumentDisplay display, BoardPoint pointer)
    {
        var pose = display.Pose;
        var local = pose.ToLocal(pointer);
        if (display.Gauge is { } gauge)
            return GaugeDrawing.Hit(pose, gauge, local) is not null;
        return InstrumentDrawing.Contains(pose, local) ||
            InstrumentDrawing.Handles(pose).Any(handle => InstrumentPose.Distance(local, handle.Position) <= 15) ||
            pose.Kind == InstrumentKind.Compass && InstrumentPose.Distance(local, new BoardPoint(pose.Size, 0)) <= 13;
    }

    internal bool SourceGaugeWheel(Point pointer, int delta, bool coarse) =>
        mathOnSource && TryGaugeWheel(Pointer(pointer), delta, coarse);

    internal bool InstrumentDragging(bool source) => mathOnSource == source && mathDrag is not null;

    internal void CancelSourceInstrumentDrag()
    {
        if (mathOnSource)
            CancelMathDrag();
    }

    internal void UpdateMathDrag(BoardPoint pointer, bool snap)
    {
        if (mathDrag is not { } drag)
            return;
        var pose = drag.Original.Pose;
        var local = pose.ToLocal(pointer);
        if (drag.Original.Gauge is not null && drag.Handle is InstrumentHandle.GaugeAdjust or InstrumentHandle.GaugeAlign)
            UpdateGaugeDrag(drag, pointer, snap);
        else if (drag.Calibration)
            instrumentTrace = drag.Stroke with { Points = [Bound(drag.Start), Bound(pointer)] };
        else if (drag.Handle is { } handle)
        {
            var min = pose.Kind == InstrumentKind.Calculator ? 215 : pose.IsProtractor ? 90 : 40;
            var localStart = pose.ToLocal(drag.Start);
            pose = handle switch
            {
                InstrumentHandle.Move => pose with { Origin = Bound(new BoardPoint(
                    Snap(pose.Origin.X + pointer.X - drag.Start.X, snap ? 32 : 0),
                    Snap(pose.Origin.Y + pointer.Y - drag.Start.Y, snap ? 32 : 0))) },
                InstrumentHandle.Rotate => pose with { Rotation = InstrumentPose.Normalize(
                    Snap(pose.Rotation + InstrumentPose.Bearing(pose.Origin, pointer) - drag.StartBearing, snap ? 15 : 0)) },
                InstrumentHandle.Resize => pose with { Size = Math.Clamp(
                    pose.Size + (local.X - localStart.X) * (pose.Kind == InstrumentKind.FrenchCurve ? 2 : 1), min, 600) },
                InstrumentHandle.Angle => pose with { Angle = Snap(pose.ReadAngle(pointer), snap ? 15 : 1) },
                InstrumentHandle.CompassTip => MoveCompassTip(pose, drag.Start, pointer, snap),
                _ => pose with { CurveRise = Math.Clamp(pose.CurveRise + localStart.Y - local.Y, -260, 260) }
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
        else if (drag.Arc is { } arc)
        {
            arc.Follow(pointer);
            instrumentDisplay = drag.Original with { Pose = pose with { Rotation = arc.Bearing } };
            var traced = arc.Apply(AsShape(drag.Stroke, ShapeKind.Arc, pose.Origin,
                pose.ToBoard(new BoardPoint(pose.Size, 0))));
            instrumentTrace = Math.Abs(traced.Sweep) >= 1 ? traced : null;
        }
        RefreshMathInstrument();
    }

    private static InstrumentPose MoveCompassTip(InstrumentPose pose, BoardPoint start, BoardPoint pointer, bool snap)
    {
        var tip = pose.ToBoard(new BoardPoint(pose.Size, 0));
        var target = new BoardPoint(tip.X + pointer.X - start.X, tip.Y + pointer.Y - start.Y);
        return pose with
        {
            Size = Math.Clamp(Snap(InstrumentPose.Distance(pose.Origin, target), snap ? pose.UnitsPerMeasure : 0), 40, 600),
            Rotation = Snap(InstrumentPose.Bearing(pose.Origin, target), snap ? 15 : 0)
        };
    }

    internal void EndMathDrag(BoardPoint pointer, bool snap)
    {
        if (mathDrag is not { } drag)
            return;
        if (drag.Handle == InstrumentHandle.GaugeAlign)
        {
            FinishGaugeAlignment(drag, pointer);
            return;
        }
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
        if (drag.Handle is not null)
            return;
        if (completed is Shape arc)
            AddConstruction([arc]);
        else if (completed is Ink { Points.Length: > 1 } ink &&
            InstrumentPose.Distance(ink.Points[0], ink.Points[^1]) >= 2)
            AddConstruction(drag.Edge is not null
                ? LineWithOptionalLabel(ink, drag.Original.Pose, ink.Points[0], ink.Points[^1]) : [ink]);
    }

    private void AddConstruction(ImmutableArray<BoardItem> items)
    {
        if (mathOnSource)
            sourceWindow?.AddConstruction(items);
        else
            AddItems(items);
    }

    private void CancelMathDrag()
    {
        if (mathDrag is null && calibrationLength is null && !alignGauge)
            return;
        if (mathDrag is { } drag)
            instrumentDisplay = drag.Original;
        mathDrag = null;
        instrumentTrace = null;
        calibrationLength = null;
        alignGauge = false;
        RefreshMathInstrument();
    }

    private void DrawFromInstrument(object sender, RoutedEventArgs args)
    {
        if (instrumentDisplay is not { Pose: var pose })
            return;
        var style = ConstructionInk();
        if (pose.Kind == InstrumentKind.Compass)
            AddConstruction([AsShape(style, ShapeKind.Circle, pose.Origin, pose.ToBoard(new BoardPoint(pose.Size, 0)))]);
        else if (pose.IsProtractor && pose.Angle > 0)
        {
            var first = pose.ToBoard(new BoardPoint(pose.Size, 0));
            var second = pose.ToBoard(InstrumentPose.Polar(-pose.Angle, pose.Size));
            var arc = pose.ToBoard(new BoardPoint(pose.Size * .35, 0));
            var label = pose.ToBoard(InstrumentPose.Polar(-pose.Angle / 2, pose.Size * .49));
            AddConstruction([
                AsShape(style, ShapeKind.Line, pose.Origin, first),
                AsShape(style with { Id = Guid.NewGuid() }, ShapeKind.Line, pose.Origin, second),
                AsShape(style with { Id = Guid.NewGuid() }, ShapeKind.Arc, pose.Origin, arc, -pose.Angle),
                MeasureLabel(style, label, $"{pose.Angle:0.#}°")
            ]);
        }
        else if (pose.Kind == InstrumentKind.Ruler)
            AddConstruction(LineWithOptionalLabel(style, pose, pose.Origin, pose.ToBoard(new BoardPoint(pose.Size, 0))));
        else if (pose.IsSetSquare)
            AddConstruction(pose.Edges().Select(edge => (BoardItem)AsShape(style with { Id = Guid.NewGuid() },
                ShapeKind.Line, pose.ToBoard(edge.Start), pose.ToBoard(edge.End))).ToImmutableArray());
        else if (pose.Kind == InstrumentKind.FrenchCurve)
            AddConstruction([style with { Points = pose.TraceCurve(-pose.Size / 2, pose.Size / 2) }]);
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
