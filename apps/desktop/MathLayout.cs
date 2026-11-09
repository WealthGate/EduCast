using System.Globalization;
using System.Windows;
using System.Windows.Media;
using EduCast.Classroom;
using Matrix = EduCast.Classroom.Matrix;

namespace EduCast.Studio;

internal sealed class MathLayout(Brush brush)
{
    private sealed record Box(double Width, double Above, double Below, Action<DrawingContext, double, double> Draw);
    private static readonly Typeface MathFace = new("Cambria Math");
    internal static readonly Typeface TextFace = new("Segoe UI");

    public DrawingGroup Render(MathNode node, double size)
    {
        var box = Layout(node, size);
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
            box.Draw(dc, 0, box.Above);
        drawing.Freeze();
        return drawing;
    }

    private Box Layout(MathNode node, double size) => node switch
    {
        Literal literal => Glyph(literal.Value, size),
        Prefix prefix => Row(Glyph(prefix.Operator, size), Layout(prefix.Operand, size)),
        Binary { Operator: "" } binary => Row(Layout(binary.Left, size), Layout(binary.Right, size)),
        Binary binary => Row(Layout(binary.Left, size), Gap(size * .12), Glyph(binary.Operator, size),
            Gap(size * .12), Layout(binary.Right, size)),
        Group group => Wrap(Layout(group.Child, size), group.Open, group.Close, size),
        Fraction fraction => Stack(Layout(fraction.Top, size * .87), Layout(fraction.Bottom, size * .87), size),
        Scripts scripts => Script(scripts, size),
        Radical radical => Root(radical, size),
        Function fn => Row(Glyph(fn.Name, size), Gap(size * .08),
            Wrap(Layout(fn.Argument, size), '(', ')', size)),
        Matrix matrix => Wrap(Table(matrix, size), '[', ']', size),
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };

    private Box Glyph(string value, double size)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            MathFace, Math.Max(size, 5), brush, 1);
        return new Box(text.WidthIncludingTrailingWhitespace, text.Baseline, text.Height - text.Baseline,
            (dc, x, y) => dc.DrawText(text, new Point(x, y - text.Baseline)));
    }

    private static Box Gap(double width) => new(width, 0, 0, (_, _, _) => { });

    private static Box Row(params Box[] children) => new(children.Sum(box => box.Width),
        children.Max(box => box.Above), children.Max(box => box.Below), (dc, x, y) =>
        {
            foreach (var child in children)
            {
                child.Draw(dc, x, y);
                x += child.Width;
            }
        });

    private Box Wrap(Box child, char open, char close, double size)
    {
        var fontSize = Math.Max(size, (child.Above + child.Below) * .77);
        return Row(Center(Glyph(open.ToString(), fontSize), child), child,
            Center(Glyph(close.ToString(), fontSize), child));
    }

    private static Box Center(Box glyph, Box child)
    {
        var offset = (child.Below - child.Above - glyph.Below + glyph.Above) / 2;
        return new Box(glyph.Width, glyph.Above - offset, glyph.Below + offset,
            (dc, x, y) => glyph.Draw(dc, x, y + offset));
    }

    private Box Stack(Box top, Box bottom, double size)
    {
        var width = Math.Max(top.Width, bottom.Width) + size * .28;
        var axis = size * .24;
        var gap = size * .11;
        var pen = new Pen(brush, Math.Max(.85, size / 19));
        return new Box(width, axis + gap + top.Above + top.Below,
            Math.Max(0, gap + bottom.Above + bottom.Below - axis), (dc, x, y) =>
            {
                var bar = y - axis;
                top.Draw(dc, x + (width - top.Width) / 2, bar - gap - top.Below);
                bottom.Draw(dc, x + (width - bottom.Width) / 2, bar + gap + bottom.Above);
                dc.DrawLine(pen, new Point(x, bar), new Point(x + width, bar));
            });
    }

    private Box Script(Scripts scripts, double size)
    {
        var basis = Layout(scripts.Basis, size);
        var upper = scripts.Superscript is null ? Gap(0) : Layout(scripts.Superscript, size * .64);
        var lower = scripts.Subscript is null ? Gap(0) : Layout(scripts.Subscript, size * .64);
        var raise = Math.Max(size * .48, basis.Above * .6);
        var drop = Math.Max(size * .22, basis.Below * .65 + lower.Above * .28);
        return new Box(basis.Width + Math.Max(upper.Width, lower.Width),
            scripts.Superscript is null ? basis.Above : Math.Max(basis.Above, raise + upper.Above),
            scripts.Subscript is null ? basis.Below : Math.Max(basis.Below, drop + lower.Below),
            (dc, x, y) =>
            {
                basis.Draw(dc, x, y);
                upper.Draw(dc, x + basis.Width, y - raise);
                lower.Draw(dc, x + basis.Width, y + drop);
            });
    }

    private Box Root(Radical radical, double size)
    {
        var child = Layout(radical.Child, size);
        var index = radical.Index is null ? Gap(0) : Layout(radical.Index, size * .53);
        var lead = Math.Max(size * .62, index.Width + size * .22);
        var roof = child.Above + size * .06;
        var top = Math.Max(roof + size * .10, roof * .48 + index.Above);
        var pen = new Pen(brush, Math.Max(.85, size / 19)) { LineJoin = PenLineJoin.Round };
        return new Box(lead + child.Width + size * .10, top, child.Below, (dc, x, y) =>
        {
            var path = new StreamGeometry();
            using (var geo = path.Open())
            {
                geo.BeginFigure(new Point(x + lead - size * .61, y - size * .12), false, false);
                geo.PolyLineTo([
                    new Point(x + lead - size * .47, y - size * .19),
                    new Point(x + lead - size * .30, y + child.Below * .48),
                    new Point(x + lead - size * .03, y - roof),
                    new Point(x + lead + child.Width + size * .10, y - roof)
                ], true, false);
            }
            dc.DrawGeometry(null, pen, path);
            index.Draw(dc, x, y - roof * .48);
            child.Draw(dc, x + lead, y);
        });
    }

    private Box Table(Matrix matrix, double size)
    {
        var cells = matrix.Rows.Select(row => row.Select(node => Layout(node, size * .84)).ToArray()).ToArray();
        var widths = Enumerable.Range(0, cells[0].Length).Select(col => cells.Max(row => row[col].Width)).ToArray();
        var above = cells.Select(row => row.Max(cell => cell.Above)).ToArray();
        var below = cells.Select(row => row.Max(cell => cell.Below)).ToArray();
        var gap = size * .18;
        var total = above.Sum() + below.Sum() + gap * (cells.Length - 1);
        var top = total / 2 + size * .18;
        return new Box(widths.Sum() + gap * (widths.Length - 1), top, Math.Max(0, total - top),
            (dc, x, y) =>
            {
                var currentY = y - top;
                for (var row = 0; row < cells.Length; row++)
                {
                    var currentX = x;
                    for (var col = 0; col < widths.Length; col++)
                    {
                        var cell = cells[row][col];
                        cell.Draw(dc, currentX + (widths[col] - cell.Width) / 2, currentY + above[row]);
                        currentX += widths[col] + gap;
                    }
                    currentY += above[row] + below[row] + gap;
                }
            });
    }
}
