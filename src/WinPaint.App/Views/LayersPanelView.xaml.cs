using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.App.ViewModels;

namespace WinPaint.App.Views;

/// <summary>Layers panel. Code-behind only implements drag-to-reorder gestures.</summary>
public partial class LayersPanelView : UserControl
{
    private Point _dragStart;
    private LayerItemViewModel? _dragItem;

    /// <summary>Creates the view.</summary>
    public LayersPanelView()
    {
        InitializeComponent();
        List.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(List);
            _dragItem = ItemAt(e.OriginalSource as DependencyObject);
        };
        List.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
            {
                return;
            }

            var d = e.GetPosition(List) - _dragStart;
            if (Math.Abs(d.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(List, new DataObject(typeof(LayerItemViewModel), _dragItem), DragDropEffects.Move);
                _dragItem = null;
            }
        };
        List.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(LayerItemViewModel)) is LayerItemViewModel dragged
                && ItemAt(e.OriginalSource as DependencyObject) is { } target
                && DataContext is LayersViewModel vm)
            {
                vm.MoveItem(dragged, target);
            }
        };
    }

    private static LayerItemViewModel? ItemAt(DependencyObject? d)
    {
        while (d is not null and not ListBoxItem)
        {
            d = VisualTreeHelper.GetParent(d);
        }

        return (d as ListBoxItem)?.DataContext as LayerItemViewModel;
    }
}
