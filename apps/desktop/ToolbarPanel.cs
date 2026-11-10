using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Visibility = System.Windows.Visibility;

namespace EduCast.Studio;

internal sealed class ToolbarPanel : Panel
{
    private sealed class Row
    {
        internal List<UIElement> Items { get; } = [];
        internal double Width => Items.Sum(item => item.DesiredSize.Width);
        internal double Height => Items.Max(item => item.DesiredSize.Height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));

        var rows = Rows(availableSize.Width);
        return rows.Count == 0 ? default : new Size(rows.Max(row => row.Width), rows.Sum(row => row.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0d;
        foreach (var row in Rows(finalSize.Width))
        {
            var x = 0d;
            var height = row.Height;
            var extra = Math.Max(0, finalSize.Width - row.Width) / row.Items.Count;
            foreach (var item in row.Items)
            {
                var width = item.DesiredSize.Width + extra;
                item.Arrange(new Rect(x, y, width, height));
                x += width;
            }
            y += height;
        }
        return finalSize;
    }

    private List<Row> Rows(double width)
    {
        var rows = new List<Row>();
        var rowWidth = 0d;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;
            if (rows.Count == 0 || rowWidth + child.DesiredSize.Width > width + .001)
            {
                rows.Add(new Row());
                rowWidth = 0;
            }
            rows[^1].Items.Add(child);
            rowWidth += child.DesiredSize.Width;
        }

        // Move a control down if it balances two neighboring rows without adding a row.
        for (var i = rows.Count - 1; i > 0; i--)
        {
            var above = rows[i - 1];
            var below = rows[i];
            while (above.Items.Count > 1)
            {
                var last = above.Items[^1];
                var itemWidth = last.DesiredSize.Width;
                if (below.Width + itemWidth > width || above.Width - below.Width <= itemWidth)
                    break;
                above.Items.RemoveAt(above.Items.Count - 1);
                below.Items.Insert(0, last);
            }
        }
        return rows;
    }
}

internal sealed class ToolbarHeightLimit : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double height ? Math.Max(140, height * .55) : 140d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
