using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using EduCast.Classroom;

namespace EduCast.Studio;

internal static class BoardTypography
{
    private sealed record Ticker(FormattedText Text, Rect Clip, Brush Background);
    private static readonly ConditionalWeakTable<BoardText, Ticker> Tickers = new();
    private static readonly Lazy<Dictionary<string, FontFamily>> Installed = new(() => Fonts.SystemFontFamilies
        .GroupBy(font => font.Source, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase));

    internal static IEnumerable<string> FontNames => Installed.Value.Keys.Where(name => name.Length <= 128)
        .Order(StringComparer.CurrentCultureIgnoreCase);

    internal static FontFamily Family(string? font) => font is not null && Installed.Value.TryGetValue(font, out var family)
        ? family : new FontFamily("Segoe UI");
    internal static TextAlignment Alignment(TextAlign align) => align switch
    {
        TextAlign.Center => TextAlignment.Center,
        TextAlign.Right => TextAlignment.Right,
        _ => TextAlignment.Left
    };

    internal static Color ColorValue(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    internal static Brush ColorBrush(uint argb) => new SolidColorBrush(ColorValue(argb));

    internal static bool LightText(uint argb)
    {
        var color = ColorValue(argb);
        return color.R * .2126 + color.G * .7152 + color.B * .0722 >= 145;
    }

    internal static bool IsTicker(BoardText text) => text.Style is { Scroll: not TextScroll.None };

    internal static void Draw(DrawingContext dc, BoardText text, double time)
    {
        if (text.Style is not { } style)
        {
            dc.DrawText(BoardSurface.Format(text.Text, text.Size, ColorBrush(text.Argb)), new Point(text.Origin.X, text.Origin.Y));
            return;
        }
        if (!IsTicker(text))
        {
            var formatted = Format(text, style, singleLine: false);
            dc.DrawText(formatted, new Point(text.Origin.X, text.Origin.Y));
            return;
        }

        var ticker = Tickers.GetValue(text, item =>
        {
            var formatted = Format(item, item.Style!, singleLine: true);
            var background = new SolidColorBrush(LightText(item.Argb)
                ? Color.FromArgb(205, 18, 29, 44) : Color.FromArgb(220, 255, 255, 255));
            return new Ticker(formatted, new Rect(item.Origin.X, item.Origin.Y, item.Style!.Width, formatted.Height + 8), background);
        });
        var clip = ticker.Clip;
        var gap = Math.Max(60, text.Size * 2);
        var interval = Math.Max(1, ticker.Text.WidthIncludingTrailingWhitespace + gap);
        var offset = time * style.Speed % interval;
        var first = clip.X + (style.Scroll == TextScroll.Left ? -offset : offset - interval);
        dc.DrawRoundedRectangle(ticker.Background, null, clip, 5, 5);
        dc.PushClip(new RectangleGeometry(clip));
        for (var x = first; x < clip.Right; x += interval)
            dc.DrawText(ticker.Text, new Point(x, clip.Y + 4));
        dc.Pop();
    }

    private static FormattedText Format(BoardText text, BoardTextStyle style, bool singleLine)
    {
        var value = style.Case switch
        {
            TextCase.Uppercase => text.Text.ToUpper(CultureInfo.CurrentCulture),
            TextCase.Lowercase => text.Text.ToLower(CultureInfo.CurrentCulture),
            _ => text.Text
        };
        if (singleLine)
            value = value.Replace("\r", "").Replace('\n', ' ');
        var type = new Typeface(Family(style.Font), style.Italic ? FontStyles.Italic : FontStyles.Normal,
            style.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            type, text.Size, ColorBrush(text.Argb), 1);
        if (!singleLine)
        {
            formatted.MaxTextWidth = style.Width;
            formatted.TextAlignment = Alignment(style.Align);
        }
        if (style.Underline)
            formatted.SetTextDecorations(TextDecorations.Underline);
        return formatted;
    }
}
