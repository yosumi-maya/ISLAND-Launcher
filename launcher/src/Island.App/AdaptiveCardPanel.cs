using System.Windows;
using System.Windows.Controls;

namespace Island.App;

public sealed class AdaptiveCardPanel : Panel
{
    private const double MinimumCardWidth = 224;
    private const double ColumnGap = 12;
    private const double RowGap = 8;
    private int columns = 1;
    private double cardWidth;
    private double rowHeight;
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinimumCardWidth : Math.Max(0, availableSize.Width);
        columns = Math.Max(1, (int)((width + ColumnGap) / (MinimumCardWidth + ColumnGap)));
        cardWidth = Math.Max(0, (width - (columns - 1) * ColumnGap) / columns); rowHeight = 0;
        foreach (UIElement child in InternalChildren) { child.Measure(new Size(cardWidth, double.PositiveInfinity)); rowHeight = Math.Max(rowHeight, child.DesiredSize.Height); }
        var rows = (InternalChildren.Count + columns - 1) / columns;
        return new(width, rows == 0 ? 0 : rows * rowHeight + (rows - 1) * RowGap);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var index = 0; index < InternalChildren.Count; index++)
            InternalChildren[index].Arrange(new Rect(index % columns * (cardWidth + ColumnGap), index / columns * (rowHeight + RowGap), cardWidth, rowHeight));
        return finalSize;
    }
}
