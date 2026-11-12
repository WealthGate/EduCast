using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EduCast.Classroom;
using Visibility = System.Windows.Visibility;

namespace EduCast.Studio;

internal sealed class CanvasTextEditor
{
    private sealed record Choice<T>(string Label, T Value);
    private static readonly double[] Sizes = [12, 16, 20, 24, 28, 32, 36, 42, 48, 56, 64, 72, 84, 96];
    private readonly ToolbarPanel tools = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox input = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0), BorderThickness = new Thickness(2),
        BorderBrush = Brushes.DodgerBlue, VerticalContentAlignment = VerticalAlignment.Top, MaxLength = 2000,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed
    };
    private readonly ComboBox font = Picker("Font family", 135);
    private readonly ComboBox size = Picker("Font size", 62);
    private readonly ComboBox palette = Picker("Text color presets", 99);
    private readonly TextBox hex = new() { MinWidth = 83, MaxLength = 7, Margin = new Thickness(0, 0, 5, 5) };
    private readonly CheckBox bold = Toggle("B", "Bold");
    private readonly CheckBox italic = Toggle("I", "Italic");
    private readonly CheckBox underline = Toggle("U", "Underline");
    private readonly ComboBox caps = Picker("Capitalization", 109);
    private readonly ComboBox alignment = Picker("Text alignment", 98);
    private readonly ComboBox scroll = Picker("Scrolling text", 117);
    private readonly ComboBox speed = Picker("Scroll speed", 83);
    private BoardText? original;
    private BoardTextStyle? style;
    private Action<BoardText>? apply;
    private uint color;
    private bool opening;

    internal CanvasTextEditor(Panel host, Canvas overlay)
    {
        var colors = new Choice<uint>[]
        {
            new("Color presets", 0), new("Black", 0xff182735), new("White", 0xffffffff),
            new("Blue", 0xff1463cc), new("Red", 0xffb32148),
            new("Green", 0xff176644), new("Violet", 0xff6d43a6), new("Orange", 0xffbd4e08), new("Yellow", 0xfff5c421)
        };
        font.ItemsSource = BoardTypography.FontNames.ToArray();
        palette.ItemsSource = colors;
        palette.DisplayMemberPath = nameof(Choice<uint>.Label);
        caps.ItemsSource = new Choice<TextCase>[] { new("As typed", TextCase.AsTyped),
            new("UPPERCASE", TextCase.Uppercase), new("lowercase", TextCase.Lowercase) };
        alignment.ItemsSource = new Choice<TextAlign>[] { new("Align left", TextAlign.Left),
            new("Center", TextAlign.Center), new("Align right", TextAlign.Right) };
        scroll.ItemsSource = new Choice<TextScroll>[] { new("Still text", TextScroll.None),
            new("Scroll left", TextScroll.Left), new("Scroll right", TextScroll.Right) };
        speed.ItemsSource = new Choice<double>[] { new("Slow", 35), new("Medium", 70), new("Fast", 140) };
        foreach (var picker in new[] { caps, alignment, scroll, speed })
            picker.DisplayMemberPath = nameof(Choice<int>.Label);

        var applyButton = new Button { Content = "Done", ToolTip = "Apply the text (Ctrl+Enter)" };
        applyButton.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
        var cancel = new Button { Content = "Cancel" };
        foreach (var control in new UIElement[]
            { font, size, palette, hex, bold, italic, underline, caps, alignment, scroll, speed, applyButton, cancel })
            tools.Children.Add(control);
        host.Children.Add(tools);
        overlay.Children.Add(input);
        AutomationProperties.SetName(input, "Canvas text");
        AutomationProperties.SetName(hex, "Text color hex code");
        hex.ToolTip = "Enter a color as #RRGGBB";
        bold.FontWeight = FontWeights.Bold;
        italic.FontStyle = FontStyles.Italic;
        foreach (var picker in new[] { font, size, caps, alignment, scroll, speed })
            picker.SelectionChanged += (_, _) => Preview();
        foreach (var box in new[] { bold, italic, underline })
            box.Click += (_, _) => Preview();
        palette.SelectionChanged += (_, _) =>
        {
            if (!opening && palette.SelectedItem is Choice<uint> { Value: not 0 } choice)
                hex.Text = Hex(choice.Value);
        };
        hex.TextChanged += (_, _) => Preview();
        applyButton.Click += (_, _) => Commit();
        cancel.Click += (_, _) => Cancel();
    }

    internal bool Active => original is not null;
    internal bool OverEditor => input.IsMouseOver;

    internal static BoardText New(Point point, uint color, EduCast.Classroom.Visibility visibility, bool answer = false)
    {
        var x = Math.Clamp(point.X, 0, BoardSurface.BoardWidth - 120);
        var y = Math.Clamp(point.Y, 0, BoardSurface.BoardHeight - 85);
        return new BoardText(Guid.NewGuid(), visibility, answer, color, new BoardPoint(x, y), 36, "")
        {
            Style = new BoardTextStyle("Segoe UI", Math.Min(720, BoardSurface.BoardWidth - x - 8))
        };
    }

    internal void Open(BoardText text, Action<BoardText> save)
    {
        Commit();
        original = text;
        apply = save;
        style = text.Style ?? new BoardTextStyle("Segoe UI", Math.Clamp(BoardSurface.BoardWidth - text.Origin.X - 8, 100, 720));
        var placement = text.Placement ?? new ObjectPlacement(1, 0, 0);
        var x = text.Origin.X * placement.Scale + placement.X;
        var y = text.Origin.Y * placement.Scale + placement.Y;
        var width = Math.Clamp(style.Width * placement.Scale, 100, BoardSurface.BoardWidth - 8);
        input.Width = width;
        Canvas.SetLeft(input, Math.Clamp(x, 0, BoardSurface.BoardWidth - width));
        var top = Math.Clamp(y, 0, BoardSurface.BoardHeight - 110);
        Canvas.SetTop(input, top);
        input.MaxHeight = BoardSurface.BoardHeight - top;

        opening = true;
        font.SelectedItem = BoardTypography.Family(style.Font).Source;
        size.ItemsSource = Sizes.Append(text.Size).Distinct().Order().ToArray();
        size.SelectedItem = text.Size;
        palette.SelectedIndex = 0;
        color = text.Argb;
        hex.Text = Hex(color);
        bold.IsChecked = style.Bold;
        italic.IsChecked = style.Italic;
        underline.IsChecked = style.Underline;
        caps.SelectedIndex = (int)style.Case;
        alignment.SelectedIndex = (int)style.Align;
        scroll.SelectedIndex = (int)style.Scroll;
        speed.SelectedIndex = style.Speed < 50 ? 0 : style.Speed > 100 ? 2 : 1;
        input.Text = text.Text;
        opening = false;
        tools.Visibility = input.Visibility = Visibility.Visible;
        Preview();
        input.Focus();
        input.CaretIndex = input.Text.Length;
    }

    internal bool Shortcut(KeyEventArgs args)
    {
        if (!Active)
            return false;
        if (args.Key == Key.Escape)
            Cancel();
        else if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            Commit();
        else
            return false;
        args.Handled = true;
        return true;
    }

    internal void Commit()
    {
        if (original is not { } text || style is null)
            return;
        var value = input.Text;
        var result = text with
        {
            Text = value, Size = (double)size.SelectedItem, Argb = color,
            Style = style with
            {
                Font = (string?)font.SelectedItem ?? "Segoe UI",
                Bold = bold.IsChecked == true, Italic = italic.IsChecked == true, Underline = underline.IsChecked == true,
                Case = ChoiceValue(caps, TextCase.AsTyped), Align = ChoiceValue(alignment, TextAlign.Left),
                Scroll = ChoiceValue(scroll, TextScroll.None), Speed = ChoiceValue(speed, 70d)
            }
        };
        var save = apply;
        Cancel();
        if (!string.IsNullOrWhiteSpace(value))
            save?.Invoke(result);
    }

    internal void Cancel()
    {
        (original, apply, style) = (null, null, null);
        input.Visibility = tools.Visibility = Visibility.Collapsed;
    }

    private void Preview()
    {
        if (opening || original is null)
            return;
        var code = hex.Text.Trim().TrimStart('#');
        if (code.Length == 6 && uint.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = 0xff000000 | rgb;
            hex.ClearValue(Control.BorderBrushProperty);
        }
        else
            hex.BorderBrush = Brushes.IndianRed;
        input.FontFamily = BoardTypography.Family(font.SelectedItem as string);
        input.FontSize = Math.Clamp((double)size.SelectedItem * (original.Placement?.Scale ?? 1), 8, 140);
        input.FontWeight = bold.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        input.FontStyle = italic.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
        input.TextDecorations = underline.IsChecked == true ? TextDecorations.Underline : null;
        input.TextAlignment = BoardTypography.Alignment(ChoiceValue(alignment, TextAlign.Left));
        input.Foreground = input.CaretBrush = BoardTypography.ColorBrush(color);
        input.Background = BoardTypography.LightText(color) ? Brushes.MidnightBlue : Brushes.White;
        input.MinHeight = Math.Min(input.MaxHeight, Math.Max(48, input.FontSize * 1.35));
        speed.IsEnabled = ChoiceValue(scroll, TextScroll.None) != TextScroll.None;
    }

    private static string Hex(uint argb) => $"#{argb & 0xffffff:X6}";

    private static T ChoiceValue<T>(ComboBox picker, T fallback) =>
        picker.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    private static ComboBox Picker(string name, int width)
    {
        var picker = new ComboBox { MinWidth = width, ToolTip = name };
        AutomationProperties.SetName(picker, name);
        return picker;
    }

    private static CheckBox Toggle(string text, string name)
    {
        var box = new CheckBox { Content = text, ToolTip = name };
        AutomationProperties.SetName(box, name);
        return box;
    }
}
