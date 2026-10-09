using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPaint.App.Resources;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;

namespace WinPaint.App.ViewModels;

/// <summary>One row in the Layers panel.</summary>
public sealed partial class LayerItemViewModel : ObservableObject
{
    private readonly LayersViewModel _owner;
    private bool _syncing;

    internal LayerItemViewModel(LayersViewModel owner, Layer layer)
    {
        _owner = owner;
        Layer = layer;
        Sync(layer);
    }

    /// <summary>The layer.</summary>
    public Layer Layer { get; private set; }

    /// <summary>Layer name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Visibility.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Opacity percent.</summary>
    [ObservableProperty]
    public partial double OpacityPercent { get; set; }

    /// <summary>Blend mode.</summary>
    [ObservableProperty]
    public partial BlendMode BlendMode { get; set; }

    /// <summary>Thumbnail image.</summary>
    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    /// <summary>Number of live text objects (shown as a badge).</summary>
    [ObservableProperty]
    public partial int TextCount { get; set; }

    private long _thumbVersion = -1;

    internal void Sync(Layer layer)
    {
        _syncing = true;
        Layer = layer;
        Name = layer.Name;
        IsVisible = layer.Visible;
        OpacityPercent = Math.Round(layer.Opacity * 100);
        BlendMode = layer.BlendMode;
        TextCount = layer.TextObjects.Count();
        _syncing = false;
    }

    internal void UpdateThumbnail(PaintDocument doc)
    {
        if (_thumbVersion == Layer.Version && Thumbnail is not null)
        {
            return;
        }

        _thumbVersion = Layer.Version;
        const int max = 48;
        var scale = Math.Min((double)max / doc.Width, (double)max / doc.Height);
        var w = Math.Max(1, (int)Math.Round(doc.Width * scale));
        var h = Math.Max(1, (int)Math.Round(doc.Height * scale));
        var full = doc.LayerComposite(Layer);
        var small = Resampler.Resize(full, w, h, ResampleMode.HighQuality);
        Thumbnail = small.ToBitmapSource();
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (!_syncing)
        {
            _owner.SetVisibility(this, value);
        }
    }

    partial void OnOpacityPercentChanged(double value)
    {
        if (!_syncing)
        {
            _owner.SetOpacity(this, value);
        }
    }

    partial void OnBlendModeChanged(BlendMode value)
    {
        if (!_syncing)
        {
            _owner.SetBlend(this, value);
        }
    }
}

/// <summary>The Layers panel: list (top layer first) and layer commands. Every change is one undo step.</summary>
public sealed partial class LayersViewModel(MainViewModel main) : ObservableObject
{
    private bool _refreshing;

    /// <summary>Layers, top-most first (as displayed).</summary>
    public ObservableCollection<LayerItemViewModel> Items { get; } = [];

    /// <summary>All blend modes.</summary>
    public static IReadOnlyList<BlendMode> BlendModes { get; } = Enum.GetValues<BlendMode>();

    /// <summary>Selected (active) layer row.</summary>
    [ObservableProperty]
    public partial LayerItemViewModel? Selected { get; set; }

    private PaintDocument Doc => main.Document;

    /// <summary>Rebuilds rows from the document.</summary>
    public void Refresh()
    {
        _refreshing = true;
        var layers = Doc.Layers.AsEnumerable().Reverse().ToList();
        while (Items.Count > layers.Count)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        for (var i = 0; i < layers.Count; i++)
        {
            if (i < Items.Count)
            {
                Items[i].Sync(layers[i]);
            }
            else
            {
                Items.Add(new LayerItemViewModel(this, layers[i]));
            }
        }

        foreach (var item in Items)
        {
            item.UpdateThumbnail(Doc);
        }

        Selected = Items.FirstOrDefault(x => x.Layer == Doc.ActiveLayer);
        _refreshing = false;
        NotifyCommands();
    }

    partial void OnSelectedChanged(LayerItemViewModel? value)
    {
        if (_refreshing || value is null)
        {
            return;
        }

        var idx = Doc.Layers.IndexOf(value.Layer);
        if (idx >= 0 && idx != Doc.ActiveLayerIndex)
        {
            main.PrepareForCommand();
            Doc.ActiveLayerIndex = Doc.Layers.IndexOf(value.Layer);
            NotifyCommands();
        }
    }

    internal void SetVisibility(LayerItemViewModel item, bool visible)
    {
        main.PrepareForCommand();
        item.Layer.Visible = visible;
        Doc.InvalidateAll();
        Doc.Commit(visible ? "Show layer" : "Hide layer");
    }

    internal void SetOpacity(LayerItemViewModel item, double percent)
    {
        main.PrepareForCommand();
        item.Layer.Opacity = Math.Clamp(percent, 0, 100) / 100.0;
        Doc.InvalidateAll();
        Doc.Commit("Layer opacity");
    }

    internal void SetBlend(LayerItemViewModel item, BlendMode mode)
    {
        main.PrepareForCommand();
        item.Layer.BlendMode = mode;
        Doc.InvalidateAll();
        Doc.Commit("Blend mode");
    }

    [RelayCommand]
    private void Add()
    {
        main.PrepareForCommand();
        LayerOperations.Add(Doc, NextName());
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        main.PrepareForCommand();
        LayerOperations.Delete(Doc, Doc.ActiveLayerIndex);
    }

    private bool CanDelete() => Doc.Layers.Count > 1;

    [RelayCommand]
    private void Duplicate()
    {
        main.PrepareForCommand();
        LayerOperations.Duplicate(Doc, Doc.ActiveLayerIndex, NextName());
    }

    [RelayCommand(CanExecute = nameof(CanMergeDown))]
    private void MergeDown()
    {
        main.PrepareForCommand();
        LayerOperations.MergeDown(Doc, Doc.ActiveLayerIndex);
    }

    private bool CanMergeDown() => Doc.ActiveLayerIndex > 0;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        main.PrepareForCommand();
        LayerOperations.Move(Doc, Doc.ActiveLayerIndex, Doc.ActiveLayerIndex + 1);
    }

    private bool CanMoveUp() => Doc.ActiveLayerIndex < Doc.Layers.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        main.PrepareForCommand();
        LayerOperations.Move(Doc, Doc.ActiveLayerIndex, Doc.ActiveLayerIndex - 1);
    }

    private bool CanMoveDown() => Doc.ActiveLayerIndex > 0;

    /// <summary>Drag-reorder: moves the dragged row to the drop row's position.</summary>
    public void MoveItem(LayerItemViewModel dragged, LayerItemViewModel target)
    {
        ArgumentNullException.ThrowIfNull(dragged);
        ArgumentNullException.ThrowIfNull(target);
        var from = Doc.Layers.IndexOf(dragged.Layer);
        var to = Doc.Layers.IndexOf(target.Layer);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        main.PrepareForCommand();
        LayerOperations.Move(Doc, from, to);
    }

    [RelayCommand]
    private void Flatten() => main.FlattenImage();

    private string NextName()
    {
        var n = Doc.Layers.Count + 1;
        string name;
        do
        {
            name = string.Format(CultureInfo.CurrentCulture, Strings.Layer_Name, n++);
        }
        while (Doc.Layers.Any(l => l.Name == name));
        return name;
    }

    private void NotifyCommands()
    {
        DeleteCommand.NotifyCanExecuteChanged();
        MergeDownCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}
