using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace test.layouts;

/// <summary>
/// Defines constants that specify how items are aligned on the horizontal axis.
/// </summary>
public enum UniformGridLayoutItemsJustification
{
    Start,
    Center,
    End,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

/// <summary>
/// Defines constants that specify how items are sized to fill the available space.
/// </summary>
public enum UniformGridLayoutItemsStretch
{
    None,
    Fill,
    Uniform,
}

/// <summary>
/// A virtualized layout that arranges items in a uniform grid with equal sized cells.
/// </summary>
public class VirtualGridLayout : VirtualizingLayout
{
    #region Dependency Properties
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth),
        typeof(double),
        typeof(VirtualGridLayout),
        new PropertyMetadata(191.0, OnLayoutPropertyChanged)
    );

    public static readonly DependencyProperty MinItemHeightProperty = DependencyProperty.Register(
        nameof(MinItemHeight),
        typeof(double),
        typeof(VirtualGridLayout),
        new PropertyMetadata(224.0, OnLayoutPropertyChanged)
    );

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns),
        typeof(int),
        typeof(VirtualGridLayout),
        new PropertyMetadata(int.MaxValue, OnLayoutPropertyChanged)
    );

    public static readonly DependencyProperty MinColumnSpacingProperty =
        DependencyProperty.Register(
            nameof(MinColumnSpacing),
            typeof(double),
            typeof(VirtualGridLayout),
            new PropertyMetadata(17.0, OnLayoutPropertyChanged)
        );

    public static readonly DependencyProperty MinRowSpacingProperty = DependencyProperty.Register(
        nameof(MinRowSpacing),
        typeof(double),
        typeof(VirtualGridLayout),
        new PropertyMetadata(32.0, OnLayoutPropertyChanged)
    );

    public static readonly DependencyProperty ItemsJustificationProperty =
        DependencyProperty.Register(
            nameof(ItemsJustification),
            typeof(UniformGridLayoutItemsJustification),
            typeof(VirtualGridLayout),
            new PropertyMetadata(
                UniformGridLayoutItemsJustification.SpaceEvenly,
                OnLayoutPropertyChanged
            )
        );

    public static readonly DependencyProperty ItemsStretchProperty = DependencyProperty.Register(
        nameof(ItemsStretch),
        typeof(UniformGridLayoutItemsStretch),
        typeof(VirtualGridLayout),
        new PropertyMetadata(UniformGridLayoutItemsStretch.Fill, OnLayoutPropertyChanged)
    );
    #endregion

    #region Public Properties
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double MinItemHeight
    {
        get => (double)GetValue(MinItemHeightProperty);
        set => SetValue(MinItemHeightProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double MinColumnSpacing
    {
        get => (double)GetValue(MinColumnSpacingProperty);
        set => SetValue(MinColumnSpacingProperty, value);
    }

    public double MinRowSpacing
    {
        get => (double)GetValue(MinRowSpacingProperty);
        set => SetValue(MinRowSpacingProperty, value);
    }

    public UniformGridLayoutItemsJustification ItemsJustification
    {
        get => (UniformGridLayoutItemsJustification)GetValue(ItemsJustificationProperty);
        set => SetValue(ItemsJustificationProperty, value);
    }

    public UniformGridLayoutItemsStretch ItemsStretch
    {
        get => (UniformGridLayoutItemsStretch)GetValue(ItemsStretchProperty);
        set => SetValue(ItemsStretchProperty, value);
    }
    #endregion

    #region Private Fields
    private int _columns;
    private double _itemWidth;
    private double _itemHeight;
    private double _effectiveColumnSpacing;
    private Size _lastAvailableSize;
    private bool _layoutInvalid = true;
    private int _lastItemCount = -1;
    private int _cachedRowCount;
    private readonly Size _zeroSize = new(0, 0);
    private const double FloatingPointEpsilon = 0.01;
    private const int ExtraBufferItems = 1;
    private bool _significantSizeChange;
    private int _previousColumns;
    #endregion

    #region Overridden Methods
    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        int itemCount = context.ItemCount;
        if (itemCount == 0)
        {
            return _zeroSize;
        }

        bool sizeChanged = !SizeEquals(_lastAvailableSize, availableSize);

        // Calculate number of columns before updating layout
        int newColumns = CalculateColumnCount(availableSize.Width);

        // Detect significant size changes (expanding from small to large view)
        _significantSizeChange =
            sizeChanged
            && (
                (_previousColumns != 0 && newColumns > _previousColumns * 1.5)
                || Math.Abs(_lastAvailableSize.Width - availableSize.Width) > 200
            );

        if (sizeChanged || _layoutInvalid)
        {
            _lastAvailableSize = availableSize;
            CalculateLayout(availableSize);
            _layoutInvalid = false;
            _previousColumns = _columns;
        }

        int rows;
        if (itemCount != _lastItemCount || _significantSizeChange)
        {
            rows = CalculateRowCount(itemCount);
            _cachedRowCount = rows;
            _lastItemCount = itemCount;
        }
        else
        {
            rows = _cachedRowCount;
        }

        var realizationRect = context.RealizationRect;
        var visibleRange = GetVisibleRange(realizationRect, rows);
        MeasureVisibleItems(context, visibleRange, itemCount);

        double totalHeight = CalculateTotalHeight(rows);
        return new Size(availableSize.Width, totalHeight);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        int itemCount = context.ItemCount;
        if (itemCount == 0)
        {
            return finalSize;
        }

        bool sizeChanged = !SizeEquals(_lastAvailableSize, finalSize);
        if (sizeChanged || _layoutInvalid)
        {
            _lastAvailableSize = finalSize;
            CalculateLayout(finalSize);
            _layoutInvalid = false;
        }

        int rows;
        if (itemCount != _lastItemCount || _significantSizeChange)
        {
            rows = CalculateRowCount(itemCount);
            _cachedRowCount = rows;
            _lastItemCount = itemCount;

            // Reset flag after processing
            _significantSizeChange = false;
        }
        else
        {
            rows = _cachedRowCount;
        }

        var realizationRect = context.RealizationRect;
        var visibleRange = GetVisibleRange(realizationRect, rows);

        for (int rowIndex = visibleRange.StartRow; rowIndex <= visibleRange.EndRow; rowIndex++)
        {
            int itemsInRow = Math.Min(_columns, itemCount - rowIndex * _columns);
            ArrangeRow(context, rowIndex, itemsInRow, finalSize.Width);
        }

        return finalSize;
    }

    protected override void OnItemsChangedCore(
        VirtualizingLayoutContext context,
        object source,
        NotifyCollectionChangedEventArgs args
    )
    {
        base.OnItemsChangedCore(context, source, args);

        _lastItemCount = -1;

        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add:
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Reset:
                InvalidateMeasure();
                InvalidateArrange();
                break;
            case NotifyCollectionChangedAction.Move:
                InvalidateArrange();
                break;
        }
    }
    #endregion

    #region Private Methods
    private void CalculateLayout(Size availableSize)
    {
        _columns = CalculateColumnCount(availableSize.Width);
        double totalColumnSpacing = (_columns - 1) * MinColumnSpacing;
        _itemWidth = (availableSize.Width - totalColumnSpacing) / _columns;
        _itemHeight = MinItemHeight;
        _effectiveColumnSpacing = MinColumnSpacing;
    }

    private int CalculateColumnCount(double availableWidth)
    {
        if (availableWidth <= MinItemWidth)
        {
            return 1;
        }

        int calculatedColumns = Math.Max(
            1,
            (int)((availableWidth + MinColumnSpacing) / (MinItemWidth + MinColumnSpacing))
        );
        return Math.Min(calculatedColumns, MaxColumns);
    }

    private int CalculateRowCount(int itemCount)
    {
        if (itemCount <= 0 || _columns <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(itemCount / (double)_columns);
    }

    private double CalculateTotalHeight(int rows)
    {
        if (rows <= 0)
        {
            return 0;
        }

        return (rows * _itemHeight) + ((rows - 1) * MinRowSpacing);
    }

    private RowRange GetVisibleRange(Rect realizationRect, int totalRows)
    {
        // When a significant size change occurs, or if the realization rect isn't valid,
        // process all rows to ensure complete layout
        if (
            _significantSizeChange
            || totalRows <= 0
            || _itemHeight + MinRowSpacing <= 0
            || realizationRect.IsEmpty
            || realizationRect.Height < 1
        )
        {
            return new RowRange(0, totalRows - 1);
        }

        double rowHeight = _itemHeight + MinRowSpacing;
        int startRowIndex = Math.Max(0, (int)(realizationRect.Y / rowHeight) - ExtraBufferItems);
        int endRowIndex = Math.Min(
            totalRows - 1,
            (int)((realizationRect.Y + realizationRect.Height) / rowHeight) + ExtraBufferItems
        );

        return new RowRange(startRowIndex, endRowIndex);
    }

    private void MeasureVisibleItems(
        VirtualizingLayoutContext context,
        RowRange visibleRange,
        int itemCount
    )
    {
        var itemSize = new Size(_itemWidth, _itemHeight);

        // More efficient than using Enumerable.Range
        for (int rowIndex = visibleRange.StartRow; rowIndex <= visibleRange.EndRow; rowIndex++)
        {
            int baseIndex = rowIndex * _columns;
            int remainingItems = itemCount - baseIndex;

            if (remainingItems <= 0)
            {
                break;
            }

            int itemsInRow = Math.Min(_columns, remainingItems);
            for (int columnIndex = 0; columnIndex < itemsInRow; columnIndex++)
            {
                int itemIndex = baseIndex + columnIndex;
                context.GetOrCreateElementAt(itemIndex).Measure(itemSize);
            }
        }
    }

    private void ArrangeRow(
        VirtualizingLayoutContext context,
        int rowIndex,
        int itemsInRow,
        double availableWidth
    )
    {
        if (itemsInRow <= 0)
        {
            return;
        }

        double totalItemWidth = itemsInRow * _itemWidth;
        double totalSpacingWidth = (itemsInRow - 1) * _effectiveColumnSpacing;
        double rowWidth = totalItemWidth + totalSpacingWidth;
        double freeSpace = Math.Max(0, availableWidth - rowWidth);
        double rowOffset = CalculateRowOffset(itemsInRow, freeSpace);
        double yPosition = rowIndex * (_itemHeight + MinRowSpacing);
        double spacing = DetermineSpacing(freeSpace);

        int baseIndex = rowIndex * _columns;
        bool isUniform = ItemsStretch == UniformGridLayoutItemsStretch.Uniform;
        double aspectRatio = isUniform ? _itemWidth / _itemHeight : 0;

        for (int columnIndex = 0; columnIndex < itemsInRow; columnIndex++)
        {
            int itemIndex = baseIndex + columnIndex;
            var container = context.GetOrCreateElementAt(itemIndex) as UIElement;

            if (container != null)
            {
                double xPosition = rowOffset + (columnIndex * (_itemWidth + spacing));
                double width = _itemWidth;
                double height = _itemHeight;

                if (isUniform)
                {
                    width = height * aspectRatio;
                }

                container.Arrange(new Rect(xPosition, yPosition, width, height));
            }
        }
    }

    private double DetermineSpacing(double freeSpace)
    {
        return IsDistributedJustification(ItemsJustification)
            ? _effectiveColumnSpacing
            : MinColumnSpacing;
    }

    private bool IsDistributedJustification(UniformGridLayoutItemsJustification justification)
    {
        return justification == UniformGridLayoutItemsJustification.SpaceBetween
            || justification == UniformGridLayoutItemsJustification.SpaceAround
            || justification == UniformGridLayoutItemsJustification.SpaceEvenly;
    }

    private double CalculateRowOffset(int itemsInRow, double freeSpace)
    {
        if (itemsInRow <= 0)
        {
            return 0;
        }

        switch (ItemsJustification)
        {
            case UniformGridLayoutItemsJustification.Center:
                return freeSpace / 2;
            case UniformGridLayoutItemsJustification.End:
                return freeSpace;
            case UniformGridLayoutItemsJustification.SpaceBetween:
                if (itemsInRow > 1)
                {
                    _effectiveColumnSpacing = MinColumnSpacing + (freeSpace / (itemsInRow - 1));
                }
                return 0;
            case UniformGridLayoutItemsJustification.SpaceAround:
                _effectiveColumnSpacing = MinColumnSpacing + (freeSpace / itemsInRow);
                return _effectiveColumnSpacing / 2;
            case UniformGridLayoutItemsJustification.SpaceEvenly:
                double spaceCount = itemsInRow + 1;
                double spacePerItem = freeSpace / spaceCount;
                _effectiveColumnSpacing = MinColumnSpacing + spacePerItem;
                return spacePerItem;
            default:
                return 0;
        }
    }

    private static bool SizeEquals(Size size1, Size size2)
    {
        return Math.Abs(size1.Width - size2.Width) < FloatingPointEpsilon
            && Math.Abs(size1.Height - size2.Height) < FloatingPointEpsilon;
    }

    private static void OnLayoutPropertyChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    )
    {
        var layout = (VirtualGridLayout)d;
        layout._layoutInvalid = true;
        layout._lastItemCount = -1;
        layout._significantSizeChange = true;
        layout.InvalidateMeasure();
        layout.InvalidateArrange();
    }
    #endregion

    private readonly struct RowRange
    {
        public int StartRow { get; }
        public int EndRow { get; }

        public RowRange(int startRow, int endRow)
        {
            StartRow = startRow;
            EndRow = endRow;
        }
    }
}
