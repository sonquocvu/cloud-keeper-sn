using System.Windows;
using System.Windows.Controls;

namespace CloudKeeperSN.App.Controls;

public sealed class ResponsiveUniformGrid : Panel
{
    public static readonly DependencyProperty MinimumItemWidthProperty = DependencyProperty.Register(
        nameof(MinimumItemWidth), typeof(double), typeof(ResponsiveUniformGrid),
        new FrameworkPropertyMetadata(150d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty MaximumColumnsProperty = DependencyProperty.Register(
        nameof(MaximumColumns), typeof(int), typeof(ResponsiveUniformGrid),
        new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(ResponsiveUniformGrid),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(ResponsiveUniformGrid),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double MinimumItemWidth
    {
        get => (double)GetValue(MinimumItemWidthProperty);
        set => SetValue(MinimumItemWidthProperty, value);
    }

    public int MaximumColumns
    {
        get => (int)GetValue(MaximumColumnsProperty);
        set => SetValue(MaximumColumnsProperty, value);
    }

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0) return new Size();

        var columns = GetColumnCount(availableSize.Width);
        var itemWidth = GetItemWidth(availableSize.Width, columns);
        var itemHeight = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            itemHeight = Math.Max(itemHeight, child.DesiredSize.Height);
        }

        var rows = (int)Math.Ceiling(InternalChildren.Count / (double)columns);
        var desiredWidth = double.IsInfinity(availableSize.Width)
            ? columns * itemWidth + (columns - 1) * HorizontalSpacing
            : availableSize.Width;
        return new Size(desiredWidth, rows * itemHeight + Math.Max(0, rows - 1) * VerticalSpacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0) return finalSize;

        var columns = GetColumnCount(finalSize.Width);
        var itemWidth = GetItemWidth(finalSize.Width, columns);
        var itemHeight = InternalChildren.Cast<UIElement>().Max(child => child.DesiredSize.Height);

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            InternalChildren[index].Arrange(new Rect(
                column * (itemWidth + HorizontalSpacing),
                row * (itemHeight + VerticalSpacing),
                itemWidth,
                itemHeight));
        }

        return finalSize;
    }

    private int GetColumnCount(double availableWidth)
    {
        var maximum = Math.Clamp(MaximumColumns, 1, Math.Max(1, InternalChildren.Count));
        if (double.IsInfinity(availableWidth)) return maximum;
        var minimumWidth = Math.Max(1d, MinimumItemWidth);
        var spacing = Math.Max(0d, HorizontalSpacing);
        return Math.Clamp((int)Math.Floor((Math.Max(0d, availableWidth) + spacing) / (minimumWidth + spacing)), 1, maximum);
    }

    private double GetItemWidth(double availableWidth, int columns)
    {
        if (double.IsInfinity(availableWidth)) return Math.Max(1d, MinimumItemWidth);
        return Math.Max(0d, (availableWidth - Math.Max(0, columns - 1) * Math.Max(0d, HorizontalSpacing)) / columns);
    }
}
