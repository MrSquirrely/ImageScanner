using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ImageScanner.Organizer.Controls;

/// <summary>
/// A wrap panel with uniform cells that only creates containers for visible rows, so galleries
/// with hundreds of thousands of images scroll smoothly. Use inside a ListBox with
/// ScrollViewer.CanContentScroll="True".
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo {
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(160.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(190.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size extent;
    private Size viewport;
    private Point offset;
    private int itemsPerRow = 1;

    public double ItemWidth {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private int ItemCount => ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

    protected override Size MeasureOverride(Size availableSize) {
        UIElementCollection children = InternalChildren; // ensures the generator is initialised
        IItemContainerGenerator generator = ItemContainerGenerator;
        int count = ItemCount;

        double width = double.IsInfinity(availableSize.Width) ? ItemWidth * 4 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? ItemHeight * 4 : availableSize.Height;
        itemsPerRow = Math.Max(1, (int)(width / ItemWidth));
        int rows = (count + itemsPerRow - 1) / itemsPerRow;

        UpdateScrollInfo(new Size(width, height), new Size(width, rows * ItemHeight));

        if (count == 0) {
            CleanUpItems(0, -1);
            return new Size(width, height);
        }

        // Realize one extra row above and below for smoother scrolling
        int firstRow = Math.Max(0, (int)(offset.Y / ItemHeight) - 1);
        int lastRow = Math.Min(rows - 1, (int)((offset.Y + height) / ItemHeight) + 1);
        int firstIndex = firstRow * itemsPerRow;
        int lastIndex = Math.Min(count - 1, (lastRow + 1) * itemsPerRow - 1);

        GeneratorPosition start = generator.GeneratorPositionFromIndex(firstIndex);
        int childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
        using (generator.StartAt(start, GeneratorDirection.Forward, true)) {
            for (int index = firstIndex; index <= lastIndex; index++, childIndex++) {
                if (generator.GenerateNext(out bool newlyRealized) is not UIElement child) {
                    continue;
                }

                if (newlyRealized) {
                    if (childIndex >= children.Count) {
                        AddInternalChild(child);
                    }
                    else {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(ItemWidth, ItemHeight));
            }
        }

        CleanUpItems(firstIndex, lastIndex);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize) {
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (int childIndex = 0; childIndex < InternalChildren.Count; childIndex++) {
            int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            int row = itemIndex / itemsPerRow;
            int column = itemIndex % itemsPerRow;
            InternalChildren[childIndex].Arrange(new Rect(column * ItemWidth - offset.X, row * ItemHeight - offset.Y, ItemWidth, ItemHeight));
        }

        return finalSize;
    }

    private void CleanUpItems(int firstIndex, int lastIndex) {
        UIElementCollection children = InternalChildren;
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (int childIndex = children.Count - 1; childIndex >= 0; childIndex--) {
            GeneratorPosition position = new(childIndex, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex < firstIndex || itemIndex > lastIndex) {
                generator.Remove(position, 1);
                RemoveInternalChildRange(childIndex, 1);
            }
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args) {
        switch (args.Action) {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Reset:
                SetVerticalOffset(0);
                break;
        }
    }

    protected override void BringIndexIntoView(int index) {
        if (itemsPerRow <= 0) {
            return;
        }

        double top = index / itemsPerRow * ItemHeight;
        if (top < offset.Y) {
            SetVerticalOffset(top);
        }
        else if (top + ItemHeight > offset.Y + viewport.Height) {
            SetVerticalOffset(top + ItemHeight - viewport.Height);
        }
    }

    private void UpdateScrollInfo(Size newViewport, Size newExtent) {
        if (newViewport == viewport && newExtent == extent) {
            return;
        }

        viewport = newViewport;
        extent = newExtent;
        double maxOffset = Math.Max(0, extent.Height - viewport.Height);
        if (offset.Y > maxOffset) {
            offset.Y = maxOffset;
        }

        ScrollOwner?.InvalidateScrollInfo();
    }

    #region IScrollInfo

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => extent.Width;
    public double ExtentHeight => extent.Height;
    public double ViewportWidth => viewport.Width;
    public double ViewportHeight => viewport.Height;
    public double HorizontalOffset => offset.X;
    public double VerticalOffset => offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(offset.Y - ItemHeight / 3);
    public void LineDown() => SetVerticalOffset(offset.Y + ItemHeight / 3);
    public void PageUp() => SetVerticalOffset(offset.Y - viewport.Height);
    public void PageDown() => SetVerticalOffset(offset.Y + viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(offset.Y - ItemHeight * 0.75);
    public void MouseWheelDown() => SetVerticalOffset(offset.Y + ItemHeight * 0.75);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void SetHorizontalOffset(double value) { }

    public void SetVerticalOffset(double value) {
        double clamped = Math.Clamp(value, 0, Math.Max(0, extent.Height - viewport.Height));
        if (Math.Abs(clamped - offset.Y) < 0.1) {
            return;
        }

        offset.Y = clamped;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle) {
        // Walk up from the focused element to the container that is our direct child
        DependencyObject? current = visual;
        while (current != null && current != this) {
            if (current is UIElement element && InternalChildren.IndexOf(element) is int childIndex and >= 0) {
                BringIndexIntoView(ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0)));
                break;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return rectangle;
    }

    #endregion
}
