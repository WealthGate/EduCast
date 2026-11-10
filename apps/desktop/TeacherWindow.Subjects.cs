using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using EduCast.Classroom;
using WpfVisibility = System.Windows.Visibility;

namespace EduCast.Studio;

public partial class TeacherWindow
{
    private sealed record SubjectTool(string Label, Tool? Shape = null, DiagramKind? Diagram = null, string? Symbol = null);

    private static readonly SubjectTool[] Shapes =
    [
        new("Line", Tool.Line), new("Rectangle", Tool.Rectangle), new("Triangle", Tool.Triangle),
        new("Circle", Tool.Circle), new("Ellipse", Tool.Ellipse), new("Arc", Tool.Arc), new("Arrow", Tool.Arrow)
    ];

    private static readonly ColorChoice[] Backgrounds =
    [
        new("Snow", 0xfffafcfa), new("Cream", 0xfffff2d5), new("Sky", 0xffe1f0ff), new("Mint", 0xffe0f2e6),
        new("Lavender", 0xfff1e8ff), new("Rose", 0xffffe9ec), new("Slate", 0xff26364a),
        new("Chalk green", 0xff153d34), new("Charcoal", 0xff202124)
    ];

    private BoardSubject? displayedSubject;
    private Ink? lastSketch;
    private long lastSketchAt;
    private (Shape Shape, Ink Current, Ink? Previous)? sketchSuggestion;

    private static IEnumerable<SubjectTool> ToolsFor(BoardSubject subject)
    {
        SubjectTool[] featured = subject switch
        {
            BoardSubject.Chemistry =>
            [
                new(DiagramLabel(DiagramKind.Atom), Diagram: DiagramKind.Atom),
                new(DiagramLabel(DiagramKind.HydrogenMolecule), Diagram: DiagramKind.HydrogenMolecule),
                new(DiagramLabel(DiagramKind.OxygenMolecule), Diagram: DiagramKind.OxygenMolecule),
                new(DiagramLabel(DiagramKind.NitrogenMolecule), Diagram: DiagramKind.NitrogenMolecule),
                new(DiagramLabel(DiagramKind.Water), Diagram: DiagramKind.Water),
                new(DiagramLabel(DiagramKind.CarbonDioxide), Diagram: DiagramKind.CarbonDioxide),
                new(DiagramLabel(DiagramKind.SodiumChloride), Diagram: DiagramKind.SodiumChloride),
                new("Single bond", Tool.Line), new("Double bond", Tool.DoubleBond), new("Triple bond", Tool.TripleBond),
                new("Electron dot (•)", Symbol: "•"),
                new("Equilibrium arrow (⇌)", Symbol: "⇌"), new("Electron (e⁻)", Symbol: "e⁻")
            ],
            BoardSubject.Biology =>
            [
                new(DiagramLabel(DiagramKind.PlantCell), Diagram: DiagramKind.PlantCell),
                new(DiagramLabel(DiagramKind.AnimalCell), Diagram: DiagramKind.AnimalCell),
                new(DiagramLabel(DiagramKind.PunnettSquare), Diagram: DiagramKind.PunnettSquare),
                new("Cross (×)", Symbol: "×")
            ],
            BoardSubject.Physics =>
            [
                new("Resistor", Tool.Resistor), new("Capacitor", Tool.Capacitor),
                new(DiagramLabel(DiagramKind.SeriesCircuit), Diagram: DiagramKind.SeriesCircuit),
                new(DiagramLabel(DiagramKind.TransverseWave), Diagram: DiagramKind.TransverseWave),
                new(DiagramLabel(DiagramKind.Refraction), Diagram: DiagramKind.Refraction),
                new("Ohm (Ω)", Symbol: "Ω"), new("Change (Δ)", Symbol: "Δ"), new("Angle (θ)", Symbol: "θ"),
                new("Wavelength (λ)", Symbol: "λ"), new("Micro (μ)", Symbol: "μ"), new("Degrees (°)", Symbol: "°")
            ],
            BoardSubject.Geography =>
            [
                new(DiagramLabel(DiagramKind.CompassRose), Diagram: DiagramKind.CompassRose),
                new(DiagramLabel(DiagramKind.ContourMap), Diagram: DiagramKind.ContourMap),
                new(DiagramLabel(DiagramKind.WaterCycle), Diagram: DiagramKind.WaterCycle)
            ],
            BoardSubject.Mathematics =>
            [
                new("Equals sign", Tool.Equal), new("Pi (π)", Symbol: "π"), new("Plus or minus (±)", Symbol: "±"),
                new("Less than or equal (≤)", Symbol: "≤"), new("Greater than or equal (≥)", Symbol: "≥")
            ],
            _ => []
        };
        return featured.Concat(Shapes);
    }

    private static string DiagramLabel(DiagramKind kind) => kind switch
    {
        DiagramKind.Atom => "Atom and electron shells",
        DiagramKind.HydrogenMolecule => "H₂ · single bond",
        DiagramKind.OxygenMolecule => "O₂ · double bond",
        DiagramKind.NitrogenMolecule => "N₂ · triple bond",
        DiagramKind.Water => "H₂O · bent molecule",
        DiagramKind.CarbonDioxide => "CO₂ · linear molecule",
        DiagramKind.SodiumChloride => "NaCl · ionic attraction",
        DiagramKind.PlantCell => "Plant cell",
        DiagramKind.AnimalCell => "Animal cell",
        DiagramKind.PunnettSquare => "Punnett square",
        DiagramKind.SeriesCircuit => "Series circuit",
        DiagramKind.TransverseWave => "Transverse wave",
        DiagramKind.Refraction => "Refraction (air to water)",
        DiagramKind.CompassRose => "Compass rose",
        DiagramKind.ContourMap => "Contour map example",
        DiagramKind.WaterCycle => "Water cycle",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private void RefreshSubjectTools()
    {
        var page = session.Page;
        SubjectChoice.SelectedItem = page.Subject;
        BoardColorButton.Content = page.Background is { } color ? $"#{color & 0xffffff:X6}…" : "Board color…";
        if (displayedSubject == page.Subject)
            return;
        displayedSubject = page.Subject;
        InsertEquationButton.Content = page.Subject == BoardSubject.Chemistry ? "Chemical equation…" : "Equation…";
        SubjectTools.ItemsSource = ToolsFor(page.Subject).ToArray();
        SubjectTools.SelectedIndex = 0;
    }

    private void ChangeSubject(object sender, SelectionChangedEventArgs args)
    {
        if (!ready || updating || SubjectChoice.SelectedItem is not BoardSubject subject || subject == session.Page.Subject)
            return;
        CancelGesture();
        session.ChangePage(session.Page with { Subject = subject });
        Refresh();
    }

    private void UseSubjectTool(object sender, RoutedEventArgs args)
    {
        if (SubjectTools.SelectedItem is not SubjectTool choice)
            return;
        if (choice.Shape is { } shape)
        {
            CancelGesture();
            ClearSelection();
            SetTool(shape);
            Refresh();
        }
        else if (choice.Diagram is { } diagram)
        {
            var settings = diagram == DiagramKind.Atom ? ConfigureDiagram(diagram, "O", true) : (null, true);
            if (settings is { } values)
                BeginPlacement(origin => new TeachingDiagram(Guid.NewGuid(), newVisibility, newAnswer, InkColor,
                    origin, diagram, values.Item2, values.Item1));
        }
        else if (choice.Symbol is { } symbol)
            BeginPlacement(origin => new BoardText(Guid.NewGuid(), newVisibility, newAnswer, InkColor, origin, 43, symbol));
    }

    private void EditDiagram(TeachingDiagram diagram)
    {
        if (ConfigureDiagram(diagram.Diagram, diagram.Element, diagram.ShowLabels) is { } values)
            Replace(diagram with { Element = values.Element, ShowLabels = values.Labels });
    }

    private (string? Element, bool Labels)? ConfigureDiagram(DiagramKind diagram, string? element, bool labels)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var dialog = BoardOptions(DiagramLabel(diagram), panel);
        var elements = new ComboBox { ItemsSource = Chemistry.ShellElements, SelectedItem = element };
        if (diagram == DiagramKind.Atom)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Neutral atoms · H through Ca · simplified shell model",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            AutomationProperties.SetName(elements, "Element symbol");
            panel.Children.Add(elements);
        }
        var show = new CheckBox { Content = "Show explanatory labels", IsChecked = labels, Margin = new Thickness(0, 8, 0, 8) };
        if (diagram is DiagramKind.HydrogenMolecule or DiagramKind.OxygenMolecule or DiagramKind.NitrogenMolecule)
            show.IsEnabled = false;
        panel.Children.Add(show);
        AddDialogActions(dialog, panel, "Apply");
        return dialog.ShowDialog() == true ? (elements.SelectedItem as string, show.IsChecked == true) : null;
    }

    private void ChooseBoardColor(object sender, RoutedEventArgs args)
    {
        CancelGesture();
        var panel = new StackPanel { Margin = new Thickness(16) };
        var dialog = BoardOptions("Board background color", panel);
        var presets = new WrapPanel { Margin = new Thickness(0, 4, 0, 7) };
        panel.Children.Add(presets);
        panel.Children.Add(new TextBlock { Text = "Hex color (#RRGGBB) · empty uses the paper default" });
        var entry = new TextBox { MaxLength = 7, Margin = new Thickness(0, 6, 0, 6) };
        AutomationProperties.SetName(entry, "Board background hex color");
        panel.Children.Add(entry);
        var preview = new Border { Height = 32, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(preview);
        var apply = AddDialogActions(dialog, panel, "Apply");
        uint? background = null;
        void Preview()
        {
            var value = entry.Text.Trim().TrimStart('#');
            var rgb = 0u;
            apply.IsEnabled = entry.Text.Length == 0 || value.Length == 6 &&
                uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
            background = apply.IsEnabled && value.Length == 6 ? 0xff000000 | rgb : null;
            var color = BoardSurface.PaperColor(session.Page.Paper, background);
            preview.Background = new SolidColorBrush(Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color));
        }

        foreach (var preset in Backgrounds)
        {
            var button = new Button { Content = preset.Name };
            button.Click += (_, _) => entry.Text = $"#{preset.Argb & 0xffffff:X6}";
            presets.Children.Add(button);
        }
        var reset = new Button { Content = "Paper default" };
        reset.Click += (_, _) => entry.Clear();
        presets.Children.Add(reset);
        entry.TextChanged += (_, _) => Preview();
        if (session.Page.Background is { } custom)
            entry.Text = $"#{custom & 0xffffff:X6}";
        Preview();
        if (dialog.ShowDialog() != true || background == session.Page.Background)
            return;
        session.ChangePage(session.Page with { Background = background });
        Refresh();
    }

    private Window BoardOptions(string title, Panel panel) => new()
    {
        Owner = this, Title = title, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Width = 425, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        Background = (Brush)FindResource("Panel"), Foreground = (Brush)FindResource("Ink"), Content = panel
    };

    private Button AddDialogActions(Window dialog, Panel panel, string applyText)
    {
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 7, 0, 0) };
        var apply = new Button { Content = applyText, IsDefault = true, Style = (Style)FindResource("Primary") };
        apply.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(apply);
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        panel.Children.Add(actions);
        return apply;
    }

    private void CollectSketch(Ink ink)
    {
        var previous = session.Page.Items.Length > 0 && ReferenceEquals(session.Page.Items[^1], lastSketch) &&
            Environment.TickCount64 - lastSketchAt < 2400 ? lastSketch : null;
        ClearSketch();
        AddItem(ink);
        if (!ReferenceEquals(session.Page.Items[^1], ink))
            return;
        lastSketch = ink;
        lastSketchAt = Environment.TickCount64;
        if (SketchRecognition.Recognize(ink, previous, session.Page.Subject) is not { } shape)
            return;
        sketchSuggestion = (shape, ink,
            shape.ShapeKind is ShapeKind.Capacitor or ShapeKind.Equal or ShapeKind.DoubleBond ? previous : null);
        var label = shape.ShapeKind switch
        {
            ShapeKind.Equal => "equals sign",
            ShapeKind.DoubleBond => "double bond",
            _ => shape.ShapeKind.ToString().ToLowerInvariant()
        };
        AcceptSketchButton.Content = $"Use {label}";
        AcceptSketchButton.Visibility = WpfVisibility.Visible;
        KeepSketchButton.Visibility = WpfVisibility.Visible;
    }

    private void AcceptSketch(object sender, RoutedEventArgs args)
    {
        var suggestion = sketchSuggestion;
        ClearSketch();
        if (suggestion is not { } item)
            return;
        var items = session.Page.Items;
        if (items.Length == 0 || !ReferenceEquals(items[^1], item.Current))
            return;
        if (item.Previous is not null)
        {
            if (items.Length < 2 || !ReferenceEquals(items[^2], item.Previous))
                return;
            items = items.RemoveAt(items.Length - 2);
        }
        session.ChangePage(session.Page with { Items = items.SetItem(items.Length - 1, item.Shape) });
        Refresh();
    }

    private void KeepSketch(object sender, RoutedEventArgs args) => ClearSketch();

    private void ClearSketch()
    {
        lastSketch = null;
        sketchSuggestion = null;
        AcceptSketchButton.Visibility = WpfVisibility.Collapsed;
        KeepSketchButton.Visibility = WpfVisibility.Collapsed;
    }
}
