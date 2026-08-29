using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Atlas.App.Controls;

public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty TargetItemWidthProperty = DependencyProperty.Register(nameof(TargetItemWidth), typeof(double), typeof(VirtualizingWrapPanel), new FrameworkPropertyMetadata(278d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel), new FrameworkPropertyMetadata(244d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(VirtualizingWrapPanel), new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private int _columns = 1;
    private double _actualItemWidth = 278;

    public double TargetItemWidth { get => (double)GetValue(TargetItemWidthProperty); set => SetValue(TargetItemWidthProperty, value); }
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return availableSize;
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0 ? Math.Max(1, owner.ActualWidth) : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) || availableSize.Height <= 0 ? Math.Max(1, ScrollOwner?.ViewportHeight ?? owner.ActualHeight) : availableSize.Height;
        _columns = Math.Max(1, (int)Math.Floor((width + Spacing) / (Math.Max(180, TargetItemWidth) + Spacing)));
        _actualItemWidth = Math.Max(210, (width - ((_columns - 1) * Spacing)) / _columns);
        var rowHeight = ItemHeight + Spacing;
        var rows = (int)Math.Ceiling(owner.Items.Count / (double)_columns);
        UpdateScrollInfo(new Size(width, rows == 0 ? 0 : Math.Max(0, rows * rowHeight - Spacing)), new Size(width, height));
        var firstRow = Math.Max(0, (int)Math.Floor(VerticalOffset / rowHeight) - 1);
        var visibleRows = Math.Max(1, (int)Math.Ceiling(height / rowHeight) + 2);
        var firstIndex = Math.Min(owner.Items.Count, firstRow * _columns);
        var lastIndex = Math.Min(owner.Items.Count - 1, ((firstRow + visibleRows) * _columns) - 1);
        Cleanup(firstIndex, lastIndex);
        if (lastIndex >= firstIndex) Generate(firstIndex, lastIndex);
        foreach (UIElement child in InternalChildren) child.Measure(new Size(_actualItemWidth, ItemHeight));
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return finalSize;
        foreach (UIElement child in InternalChildren)
        {
            var index = owner.ItemContainerGenerator.IndexFromContainer(child);
            if (index < 0) continue;
            child.Arrange(new Rect(index % _columns * (_actualItemWidth + Spacing), index / _columns * (ItemHeight + Spacing) - VerticalOffset, _actualItemWidth, ItemHeight));
        }
        return finalSize;
    }

    private void Generate(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;
        var start = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
        using (generator.StartAt(start, GeneratorDirection.Forward, true))
            for (var itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var isNew);
                if (!isNew) continue;
                if (childIndex >= InternalChildren.Count) AddInternalChild(child); else InsertInternalChild(childIndex, child);
                generator.PrepareItemContainer(child);
            }
    }

    private void Cleanup(int firstIndex, int lastIndex)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return;
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var itemIndex = owner.ItemContainerGenerator.IndexFromContainer(InternalChildren[childIndex]);
            if (itemIndex >= firstIndex && itemIndex <= lastIndex) continue;
            ItemContainerGenerator.Remove(new GeneratorPosition(childIndex, 0), 1);
            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        var changed = extent != _extent || viewport != _viewport;
        _extent = extent; _viewport = viewport; _offset.X = 0;
        _offset.Y = Math.Max(0, Math.Min(_offset.Y, Math.Max(0, ExtentHeight - ViewportHeight)));
        if (changed) ScrollOwner?.InvalidateScrollInfo();
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }
    public void LineUp() => SetVerticalOffset(VerticalOffset - 42);
    public void LineDown() => SetVerticalOffset(VerticalOffset + 42);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 126);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 126);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void LineLeft() { } public void LineRight() { } public void MouseWheelLeft() { } public void MouseWheelRight() { } public void PageLeft() { } public void PageRight() { } public void SetHorizontalOffset(double offset) { }
    public void SetVerticalOffset(double offset) { var value = Math.Max(0, Math.Min(offset, Math.Max(0, ExtentHeight - ViewportHeight))); if (Math.Abs(value - _offset.Y) < .1) return; _offset.Y = value; ScrollOwner?.InvalidateScrollInfo(); InvalidateMeasure(); }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null || visual is not DependencyObject container) return rectangle;
        var index = owner.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0) return rectangle;
        var top = index / _columns * (ItemHeight + Spacing);
        if (top < VerticalOffset) SetVerticalOffset(top); else if (top + ItemHeight > VerticalOffset + ViewportHeight) SetVerticalOffset(top + ItemHeight - ViewportHeight);
        return new Rect(0, top, _actualItemWidth, ItemHeight);
    }
}
