using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using EduCast.Classroom;

namespace EduCast.Studio;

internal enum InsertKind { Equation, Graph, Text, Chemistry }

internal sealed class ObjectDialog : Window
{
    private readonly TextBox entry;
    private readonly TextBlock feedback;
    private readonly BoardSurface preview = new();
    private readonly Button insert;
    private readonly InsertKind kind;

    public ObjectDialog(Window owner, InsertKind kind, string initial, bool editing, bool reviewing = false)
    {
        this.kind = kind;
        Owner = owner;
        Title = (reviewing ? "Review handwritten " : editing ? "Edit " : "Insert ") +
            (kind == InsertKind.Chemistry ? "chemical equation" : kind.ToString().ToLowerInvariant());
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 610;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Panel");
        SetResourceReference(ForegroundProperty, "Ink");
        FontSize = 14;

        var root = new StackPanel { Margin = new Thickness(17) };
        var help = kind switch
        {
            InsertKind.Equation => "Use ^ and _ for scripts, / for fractions, sqrt(x), root(3,x), or matrix(a,b;c,d).",
            InsertKind.Graph => "Enter a function such as y = x^2 - 4x + 3. Trigonometry uses radians.",
            InsertKind.Chemistry => "Type H2O, Ca(OH)2, or 2H2(g) + O2(g) -> 2H2O(l). Use ^ for charges (Fe^3+, Cl^-) and <=> for equilibrium.",
            _ => "Type the text you want to place on the board."
        };
        root.Children.Add(new TextBlock
        {
            Text = reviewing ? "Check the suggested equation. Replace any ? with the intended character. " + help : help,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 9)
        });
        entry = new TextBox
        {
            Text = initial, MaxLength = kind == InsertKind.Text ? 2000 : MathParser.MaxLength,
            AcceptsReturn = kind == InsertKind.Text,
            MinHeight = kind == InsertKind.Text ? 75 : 34, MaxHeight = 130,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(entry, $"{kind} source");
        root.Children.Add(entry);
        feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 7) };
        root.Children.Add(feedback);
        var view = new Viewbox { Child = preview, Stretch = Stretch.Uniform };
        root.Children.Add(new Border
        {
            Child = view, Background = Brushes.Black, Height = 210,
            Margin = new Thickness(0, 0, 0, 10), ClipToBounds = true
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        insert = new Button
        {
            Content = editing ? "Apply" : "Place on board",
            IsDefault = kind != InsertKind.Text, Style = (Style)Application.Current.FindResource("Primary")
        };
        insert.Click += (_, _) => DialogResult = true;
        actions.Children.Add(insert);
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        root.Children.Add(actions);
        Content = root;
        entry.TextChanged += (_, _) => UpdatePreview();
        Loaded += (_, _) => { entry.Focus(); entry.SelectAll(); };
        UpdatePreview();
    }

    public string Source => entry.Text.Trim();

    private void UpdatePreview()
    {
        var error = "";
        bool? balanced = null;
        var valid = kind switch
        {
            InsertKind.Equation => MathParser.TryParse(Source, out _, out error),
            InsertKind.Graph => MathParser.TryParseGraph(Source, out _, out error),
            InsertKind.Chemistry => Chemistry.TryFormat(Source, out _, out balanced, out error),
            _ => Source.Length > 0
        };
        insert.IsEnabled = valid;
        feedback.SetResourceReference(TextBlock.ForegroundProperty, valid && balanced != false ? "SuccessInk" : "WarningInk");
        feedback.Text = balanced switch
        {
            true => "Balanced by atom count and net charge.",
            false => "Unbalanced. Check the coefficients and charges.",
            _ => error
        };
        var origin = kind == InsertKind.Graph ? new BoardPoint(480, 184) : new BoardPoint(48, 270);
        var item = Create(kind, Source, origin, 0xff152b38, Classroom.Visibility.Both, false);
        preview.Edit(new Classroom.Page("", BoardPaper.Plain, false, valid ? [item] : []));
    }

    public static Classroom.Content Create(InsertKind kind, string source, BoardPoint origin, uint color,
        Classroom.Visibility visibility, bool answer) => kind switch
    {
        InsertKind.Equation => new Equation(Guid.NewGuid(), visibility, answer, color, origin, 48, source),
        InsertKind.Graph => new Graph(Guid.NewGuid(), visibility, answer, 0xff155e76, origin, source),
        InsertKind.Chemistry => new ChemicalEquation(Guid.NewGuid(), visibility, answer, color, origin, 43, source),
        _ => new BoardText(Guid.NewGuid(), visibility, answer, color, origin, 36, source)
    };
}
