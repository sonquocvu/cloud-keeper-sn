using System.Windows;
using System.Windows.Controls;

namespace CloudKeeperSN.App.Controls;

public sealed class AdaptiveTwoPanePanel : Panel
{
    public static readonly DependencyProperty BreakpointProperty = DependencyProperty.Register(
        nameof(Breakpoint), typeof(double), typeof(AdaptiveTwoPanePanel),
        new FrameworkPropertyMetadata(900d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveTwoPanePanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty PrimaryFractionProperty = DependencyProperty.Register(
        nameof(PrimaryFraction), typeof(double), typeof(AdaptiveTwoPanePanel),
        new FrameworkPropertyMetadata(0.34d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty NarrowPrimaryFractionProperty = DependencyProperty.Register(
        nameof(NarrowPrimaryFraction), typeof(double), typeof(AdaptiveTwoPanePanel),
        new FrameworkPropertyMetadata(0.4d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Breakpoint { get => (double)GetValue(BreakpointProperty); set => SetValue(BreakpointProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public double PrimaryFraction { get => (double)GetValue(PrimaryFractionProperty); set => SetValue(PrimaryFractionProperty, value); }
    public double NarrowPrimaryFraction { get => (double)GetValue(NarrowPrimaryFractionProperty); set => SetValue(NarrowPrimaryFractionProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0) return new Size();
        var gap = Math.Max(0d, Spacing);
        var wide = availableSize.Width >= Math.Max(1d, Breakpoint);
        if (wide)
        {
            var usableWidth = Math.Max(0d, availableSize.Width - gap);
            var primaryWidth = usableWidth * Math.Clamp(PrimaryFraction, 0.25d, 0.5d);
            InternalChildren[0].Measure(new Size(primaryWidth, availableSize.Height));
            if (InternalChildren.Count > 1)
                InternalChildren[1].Measure(new Size(Math.Max(0d, usableWidth - primaryWidth), availableSize.Height));
            return new Size(availableSize.Width, InternalChildren.Cast<UIElement>().Max(child => child.DesiredSize.Height));
        }

        if (double.IsInfinity(availableSize.Height))
        {
            foreach (UIElement child in InternalChildren) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            return new Size(availableSize.Width, InternalChildren.Cast<UIElement>().Sum(child => child.DesiredSize.Height) + gap);
        }

        var usableHeight = Math.Max(0d, availableSize.Height - gap);
        var primaryHeight = usableHeight * Math.Clamp(NarrowPrimaryFraction, 0.3d, 0.55d);
        InternalChildren[0].Measure(new Size(availableSize.Width, primaryHeight));
        if (InternalChildren.Count > 1)
            InternalChildren[1].Measure(new Size(availableSize.Width, Math.Max(0d, usableHeight - primaryHeight)));
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0) return finalSize;
        var gap = Math.Max(0d, Spacing);
        var wide = finalSize.Width >= Math.Max(1d, Breakpoint);
        if (wide)
        {
            var usableWidth = Math.Max(0d, finalSize.Width - gap);
            var primaryWidth = usableWidth * Math.Clamp(PrimaryFraction, 0.25d, 0.5d);
            InternalChildren[0].Arrange(new Rect(0, 0, primaryWidth, finalSize.Height));
            if (InternalChildren.Count > 1)
                InternalChildren[1].Arrange(new Rect(primaryWidth + gap, 0, Math.Max(0d, usableWidth - primaryWidth), finalSize.Height));
        }
        else
        {
            var usableHeight = Math.Max(0d, finalSize.Height - gap);
            var primaryHeight = usableHeight * Math.Clamp(NarrowPrimaryFraction, 0.3d, 0.55d);
            InternalChildren[0].Arrange(new Rect(0, 0, finalSize.Width, primaryHeight));
            if (InternalChildren.Count > 1)
                InternalChildren[1].Arrange(new Rect(0, primaryHeight + gap, finalSize.Width, Math.Max(0d, usableHeight - primaryHeight)));
        }
        return finalSize;
    }
}
