using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WinPaint.App.Controls;

/// <summary>
/// A labeled toolbar group that can collapse into a single dropdown button when the window is narrow
/// (like Windows 11 Paint compacts its toolbar).
/// </summary>
[System.Windows.Markup.ContentProperty(nameof(Content))]
public sealed class ToolbarGroup : FrameworkElement
{
    /// <summary>Group content.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(object), typeof(ToolbarGroup));

    /// <summary>Group label.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(object), typeof(ToolbarGroup));

    /// <summary>Compact (collapsed into a dropdown).</summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(ToolbarGroup), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Icon shown when compact.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(ToolbarGroup));

    /// <summary>Collapse priority (lower collapses first).</summary>
    public static readonly DependencyProperty PriorityProperty = DependencyProperty.Register(
        nameof(Priority), typeof(int), typeof(ToolbarGroup), new PropertyMetadata(0));

    private readonly ContentPresenter _inline = new();
    private readonly ContentPresenter _popupContent = new();
    private readonly ToggleButton _compactButton;
    private readonly Popup _popup;
    private readonly TextBlock _label;
    private readonly StackPanel _full;
    private readonly Grid _root = new();

    /// <summary>Creates the group.</summary>
    public ToolbarGroup()
    {
        Focusable = false;
        _label = new TextBlock();
        _label.SetResourceReference(StyleProperty, "GroupLabel");
        _full = new StackPanel { VerticalAlignment = VerticalAlignment.Stretch };
        _full.Children.Add(_inline);
        _full.Children.Add(_label);

        var icon = new System.Windows.Shapes.Path();
        icon.SetResourceReference(StyleProperty, "IconPath");
        icon.SetBinding(System.Windows.Shapes.Path.DataProperty, new System.Windows.Data.Binding(nameof(Icon)) { Source = this });
        var compactLabel = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        compactLabel.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Header)) { Source = this });
        var arrow = new System.Windows.Shapes.Path { Width = 8, Height = 8, Margin = new Thickness(0, 2, 0, 0) };
        arrow.SetResourceReference(StyleProperty, "IconPath");
        arrow.SetResourceReference(System.Windows.Shapes.Path.DataProperty, "Icon.ChevronDown");
        arrow.Width = 8;
        arrow.Height = 8;
        var compactContent = new StackPanel();
        compactContent.Children.Add(icon);
        compactContent.Children.Add(compactLabel);
        compactContent.Children.Add(arrow);
        _compactButton = new ToggleButton { Content = compactContent, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        _compactButton.SetResourceReference(StyleProperty, "ToolToggle");
        _compactButton.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(Header)) { Source = this });
        _compactButton.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new System.Windows.Data.Binding(nameof(Header)) { Source = this });

        var popupBorder = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = _popupContent };
        popupBorder.SetResourceReference(Border.BackgroundProperty, "ToolbarBackgroundBrush");
        popupBorder.SetResourceReference(Border.BorderBrushProperty, "SeparatorBrush");
        _popup = new Popup
        {
            Child = popupBorder,
            StaysOpen = false,
            PlacementTarget = _compactButton,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
        };
        _popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding(nameof(ToggleButton.IsChecked)) { Source = _compactButton, Mode = System.Windows.Data.BindingMode.TwoWay });

        _root.Children.Add(_full);
        _root.Children.Add(_compactButton);
        _root.Children.Add(_popup);
        AddVisualChild(_root);
        AddLogicalChild(_root);
        ApplyMode();
    }

    /// <summary>Group content.</summary>
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>Group label.</summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Compact mode.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>Icon for compact mode.</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Collapse priority (lower collapses first).</summary>
    public int Priority
    {
        get => (int)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }

    /// <inheritdoc/>
    protected override System.Collections.IEnumerator LogicalChildren =>
        (Content is null ? new object[] { _root } : new[] { _root, Content }).GetEnumerator();

    /// <inheritdoc/>
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc/>
    protected override Visual GetVisualChild(int index) => _root;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        _root.Measure(availableSize);
        return _root.DesiredSize;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _root.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ContentProperty)
        {
            if (e.OldValue is not null)
            {
                RemoveLogicalChild(e.OldValue);
            }

            if (e.NewValue is not null)
            {
                AddLogicalChild(e.NewValue);
            }
        }

        if (e.Property == IsCompactProperty || e.Property == ContentProperty || e.Property == HeaderProperty)
        {
            ApplyMode();
        }
    }

    private void ApplyMode()
    {
        if (_label is null)
        {
            return;
        }

        _label.Text = Header as string ?? string.Empty;
        if (IsCompact)
        {
            _inline.Content = null;
            _popupContent.Content = Content;
            _full.Visibility = Visibility.Collapsed;
            _compactButton.Visibility = Visibility.Visible;
        }
        else
        {
            _compactButton.IsChecked = false;
            _popupContent.Content = null;
            _inline.Content = Content;
            _full.Visibility = Visibility.Visible;
            _compactButton.Visibility = Visibility.Collapsed;
        }
    }
}

/// <summary>Horizontal panel that compacts the lowest-priority <see cref="ToolbarGroup"/>s when space runs out.</summary>
public sealed class CollapsingToolbarPanel : Panel
{
    private readonly Dictionary<UIElement, double> _fullWidth = [];

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        foreach (var c in children)
        {
            c.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (c is not ToolbarGroup { IsCompact: true })
            {
                _fullWidth[c] = c.DesiredSize.Width;
            }
        }

        if (!double.IsInfinity(availableSize.Width))
        {
            const double compactWidth = 56;
            var groups = children.OfType<ToolbarGroup>().OrderBy(g => g.Priority).ToList();
            double Total(ISet<ToolbarGroup> compact) => children.Sum(c => c is ToolbarGroup g && compact.Contains(g) ? compactWidth : _fullWidth.GetValueOrDefault(c, c.DesiredSize.Width));
            var compactSet = new HashSet<ToolbarGroup>();
            foreach (var g in groups)
            {
                if (Total(compactSet) <= availableSize.Width)
                {
                    break;
                }

                compactSet.Add(g);
            }

            var changed = false;
            foreach (var g in groups)
            {
                var want = compactSet.Contains(g);
                if (g.IsCompact != want)
                {
                    g.IsCompact = want;
                    changed = true;
                }
            }

            if (changed)
            {
                foreach (var c in children)
                {
                    c.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                    if (c is not ToolbarGroup { IsCompact: true })
                    {
                        _fullWidth[c] = c.DesiredSize.Width;
                    }
                }
            }
        }

        return new Size(Math.Min(children.Sum(c => c.DesiredSize.Width), double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width), children.Count == 0 ? 0 : children.Max(c => c.DesiredSize.Height));
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (UIElement c in InternalChildren)
        {
            c.Arrange(new Rect(x, 0, c.DesiredSize.Width, finalSize.Height));
            x += c.DesiredSize.Width;
        }

        return finalSize;
    }
}
