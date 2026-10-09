using CommunityToolkit.Mvvm.Input;
using WinPaint.App.Resources;
using WinPaint.App.Services;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.Tools;

namespace WinPaint.App.ViewModels;

/// <summary>Edit menu, clipboard, selection and image commands.</summary>
public sealed partial class MainViewModel
{
    private readonly IClipboardService _clipboard;

    /// <summary>Raised when the selection context menu should open.</summary>
    public event EventHandler? ContextMenuRequested;

    /// <summary>True when there is an active selection (for menu enabling).</summary>
    public bool HasSelection => ActiveSelection?.HasSelection == true;

    /// <inheritdoc/>
    public double HandleTolerance => 5 / Math.Max(0.01, Zoom);

    /// <inheritdoc/>
    public void TextFlattened() => StatusNotice = Strings.Status_TextFlattened;

    /// <inheritdoc/>
    public void ShowContextMenu() => ContextMenuRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Text.Commit();
        _activeTool.CommitPending();
        Document.Undo();
        ToolStateChanged();
    }

    private bool CanUndo() => Document.History.CanUndo || Text.Session is not null || _activeTool.HasPendingOperation;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        Text.Commit();
        _activeTool.CommitPending();
        Document.Redo();
        ToolStateChanged();
    }

    private bool CanRedo() => Document.History.CanRedo;

    [RelayCommand]
    private void Copy()
    {
        var px = ActiveSelection?.CopySelection();
        if (px is not null)
        {
            _clipboard.SetImage(px);
            ReportClipboard();
        }
    }

    [RelayCommand]
    private void Cut()
    {
        var px = ActiveSelection?.CopySelection();
        if (px is not null)
        {
            _clipboard.SetImage(px);
            ReportClipboard();
            ActiveSelection!.DeleteSelection();
            ToolStateChanged();
        }
    }

    [RelayCommand]
    private void Paste()
    {
        var img = _clipboard.GetImage();
        if (img is null)
        {
            return;
        }

        PasteImage(img);
        ReportClipboard();
    }

    /// <summary>Pastes an image as a floating selection at the top-left of the visible area.</summary>
    public void PasteImage(PixelBuffer img)
    {
        ArgumentNullException.ThrowIfNull(img);
        Text.Commit();
        _activeTool.CommitPending();
        if (img.Width > Document.Width || img.Height > Document.Height)
        {
            if (_dialogs.Confirm(Strings.Msg_PasteEnlarge))
            {
                ImageOperations.ResizeCanvas(Document, Math.Max(img.Width, Document.Width), Math.Max(img.Height, Document.Height), SecondaryColor);
            }
        }

        var visible = View?.VisibleCanvasRect ?? Document.Bounds;
        var x = visible.IsEmpty ? 0 : visible.X;
        var y = visible.IsEmpty ? 0 : visible.Y;
        if (ActiveToolKind is not (ToolKind.RectSelect or ToolKind.FreeSelect))
        {
            ActiveToolKind = ToolKind.RectSelect;
        }

        ActiveSelection!.PasteFloating(img, x, y);
        ToolStateChanged();
    }

    [RelayCommand]
    private void PasteFrom()
    {
        var path = _dialogs.PickOpenFile();
        if (path is null)
        {
            return;
        }

        try
        {
            PasteImage(Core.Imaging.Codecs.ImageCodec.Decode(path).Pixels);
        }
        catch (Core.Imaging.Codecs.ImageOpenException ex)
        {
            _dialogs.ShowError(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_OpenFailed, ex.Message));
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        Text.Commit();
        if (ActiveToolKind is not (ToolKind.RectSelect or ToolKind.FreeSelect))
        {
            ActiveToolKind = ToolKind.RectSelect;
        }

        ActiveSelection!.SelectAll();
        ToolStateChanged();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        Text.Commit();
        if (ActiveToolKind is not (ToolKind.RectSelect or ToolKind.FreeSelect))
        {
            ActiveToolKind = ToolKind.RectSelect;
        }

        ActiveSelection!.InvertSelection();
        ToolStateChanged();
    }

    /// <summary>Delete key: deletes the selected text object, the selection, or does nothing.</summary>
    [RelayCommand]
    private void Delete()
    {
        if (Text.Session is not null)
        {
            Text.Delete();
            return;
        }

        if (_activeTool is ShapeTool { IsAdjusting: true })
        {
            Document.Floating = null;
            _activeTool.CommitPending();
            ToolStateChanged();
            return;
        }

        ActiveSelection?.DeleteSelection();
        ToolStateChanged();
    }

    [RelayCommand]
    private void Crop()
    {
        Text.Commit();
        if (ActiveSelection?.HasSelection == true)
        {
            ActiveSelection.CropToSelection();
            ToolStateChanged();
        }
    }

    [RelayCommand]
    private void Rotate(OrthoTransform t)
    {
        Text.Commit();
        if (ActiveSelection?.HasSelection == true)
        {
            ActiveSelection.TransformSelection(t);
        }
        else
        {
            _activeTool.CommitPending();
            ImageOperations.Orthogonal(Document, t);
        }

        ToolStateChanged();
    }

    [RelayCommand]
    private void ResizeSkew()
    {
        Text.Commit();
        var sel = ActiveSelection?.HasSelection == true ? ActiveSelection : null;
        var bounds = sel?.SelectionBounds ?? Document.Bounds;
        if (sel is null)
        {
            _activeTool.CommitPending();
        }

        var r = _dialogs.ResizeSkew(bounds.Width, bounds.Height);
        if (r is null)
        {
            return;
        }

        if (sel is not null)
        {
            sel.ResizeSkewSelection(r.Width, r.Height, r.SkewHorizontal, r.SkewVertical);
        }
        else
        {
            if (r.Width != Document.Width || r.Height != Document.Height)
            {
                ImageOperations.Resize(Document, r.Width, r.Height);
            }

            if (r.SkewHorizontal != 0 || r.SkewVertical != 0)
            {
                ImageOperations.Skew(Document, r.SkewHorizontal, r.SkewVertical, SecondaryColor);
            }
        }

        UpdateImageSizeText();
        ToolStateChanged();
    }

    [RelayCommand]
    private void InvertColors()
    {
        Text.Commit();
        if (ActiveSelection?.HasSelection == true)
        {
            ActiveSelection.InvertSelectionColors();
        }
        else
        {
            _activeTool.CommitPending();
            ImageOperations.InvertColors(Document);
        }

        ToolStateChanged();
    }

    [RelayCommand]
    private void ToggleTransparentCanvas()
    {
        PrepareForCommand();
        var bg = Document.Layers.FirstOrDefault(l => l.IsBackground);
        if (bg is not null)
        {
            ImageOperations.SetTransparentCanvas(Document, !bg.IsTransparent);
        }

        OnPropertyChanged(nameof(IsTransparentCanvas));
    }

    /// <summary>Flattens all layers and live text into one layer after a confirmation.</summary>
    public void FlattenImage()
    {
        PrepareForCommand();
        if (Document.Layers.Count == 1 && !Document.AllText.Any())
        {
            return;
        }

        if (_dialogs.Confirm(Strings.Layer_FlattenConfirm))
        {
            LayerOperations.FlattenImage(Document);
        }
    }

    /// <summary>True when the background layer is a transparent canvas.</summary>
    public bool IsTransparentCanvas => Document.Layers.FirstOrDefault(l => l.IsBackground)?.IsTransparent ?? true;

    /// <summary>Escape: commit/cancel the current floating operation.</summary>
    public bool HandleEscape()
    {
        if (Text.Session is not null)
        {
            Text.Commit();
            return true;
        }

        var handled = _activeTool.OnKey(System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None);
        ToolStateChanged();
        return handled;
    }

    /// <summary>Forwards a key to the active tool (arrows nudge the selection, Enter commits).</summary>
    public bool ForwardKey(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
    {
        var handled = _activeTool.OnKey(key, modifiers);
        if (handled)
        {
            ToolStateChanged();
        }

        return handled;
    }

    private void UpdateSelectionText()
    {
        var sel = ActiveSelection;
        if (sel?.HasSelection == true)
        {
            var b = sel.SelectionBounds;
            SelectionText = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Status_Size, b.Width, b.Height);
        }
        else if (_activeTool is ShapeTool { IsAdjusting: true } st)
        {
            SelectionText = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Status_Size, (int)st.AdjustBounds.Width, (int)st.AdjustBounds.Height);
        }
        else
        {
            SelectionText = string.Empty;
        }

        OnPropertyChanged(nameof(HasSelection));
        UndoCommand.NotifyCanExecuteChanged();
    }

    private void ReportClipboard()
    {
        if (_clipboard.LastUsedFallback)
        {
            StatusNotice = Strings.Status_ClipboardFallback;
        }
    }
}
