using System.Windows;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Tools;

/// <summary>Selection commands (lift, commit, clipboard, crop, per-selection transforms).</summary>
public sealed partial class SelectionTool
{
    /// <summary>Selects the whole canvas (commits any floating pixels first).</summary>
    public void SelectAll()
    {
        CommitPending();
        _region = SelectionRegion.FromRect(Host.Document.Bounds);
        Host.ToolStateChanged();
    }

    /// <summary>Selects everything outside the current selection (or everything when nothing is selected).</summary>
    public void InvertSelection()
    {
        var current = IsFloating ? SelectionRegion.FromRect(SelectionBounds.Intersect(Host.Document.Bounds)) : _region;
        CommitPending();
        _region = current is null ? SelectionRegion.FromRect(Host.Document.Bounds) : current.Invert(Host.Document.Bounds);
        Host.ToolStateChanged();
    }

    /// <summary>Removes the selection without changing pixels (floating pixels are committed).</summary>
    public void Deselect() => CommitPending();

    /// <summary>Deletes the selected pixels (secondary color on an opaque background, transparent elsewhere).</summary>
    public bool DeleteSelection()
    {
        var doc = Host.Document;
        if (IsFloating)
        {
            // The source area was already cleared when lifting; dropping the pixels deletes them.
            doc.Floating = null;
            _source = _sourceOriginal = null;
            _region = null;
            doc.Commit("Delete");
            return true;
        }

        if (_region is null)
        {
            return false;
        }

        var layer = doc.ActiveLayer;
        if (SelectionOperations.FlattenTextIn(doc, layer, _region.Bounds))
        {
            Host.TextFlattened();
        }

        SelectionOperations.Clear(doc, layer, _region, Host.Settings.Secondary);
        _region = null;
        doc.Commit("Delete");
        return true;
    }

    /// <summary>The selected pixels (including rendered text) for the clipboard, or null when nothing is selected.</summary>
    public PixelBuffer? CopySelection()
    {
        if (IsFloating)
        {
            return Host.Document.Floating?.Pixels.Clone();
        }

        return _region is null ? null : SelectionOperations.Extract(Host.Document, Host.Document.ActiveLayer, _region);
    }

    /// <summary>Places pixels as a floating selection at (x, y).</summary>
    public void PasteFloating(PixelBuffer pixels, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        CommitPending();
        _layer = Host.Document.ActiveLayer;
        _sourceOriginal = pixels;
        _source = Host.Settings.TransparentSelection ? SelectionOperations.KeyOut(pixels, Host.Settings.Secondary) : pixels;
        _bounds = new Rect(x, y, pixels.Width, pixels.Height);
        _stepName = "Paste";
        UpdateFloating();
        Host.ToolStateChanged();
    }

    /// <summary>
    /// Crops the image to the selection's bounding box; for a free-form selection the area outside the shape becomes
    /// the secondary color (transparent on a transparent layer). One undo step.
    /// </summary>
    public bool CropToSelection()
    {
        var doc = Host.Document;
        if (IsFloating)
        {
            var b = SelectionBounds;
            CommitFloating(commit: false);
            _region = SelectionRegion.FromRect(b.Intersect(doc.Bounds));
        }

        if (_region is null || _region.Bounds.IsEmpty)
        {
            return false;
        }

        var region = _region;
        _region = null;
        if (region.Mask is not null)
        {
            var layer = doc.ActiveLayer;
            if (SelectionOperations.FlattenTextIn(doc, layer, region.Bounds))
            {
                Host.TextFlattened();
            }

            var outside = new bool[region.Mask.Length];
            for (var i = 0; i < outside.Length; i++)
            {
                outside[i] = !region.Mask[i];
            }

            SelectionOperations.Clear(doc, layer, SelectionRegion.FromMask(region.Bounds, outside), Host.Settings.Secondary);
        }

        ImageOperations.Crop(doc, region.Bounds, commit: true);
        return true;
    }

    /// <summary>Rotates or flips only the selection (lifting it first).</summary>
    public bool TransformSelection(OrthoTransform t)
    {
        if (!EnsureFloating("Rotate selection"))
        {
            return false;
        }

        var center = new Point(_bounds.X + (_bounds.Width / 2), _bounds.Y + (_bounds.Height / 2));
        _sourceOriginal = Transforms.Apply(_sourceOriginal!, t);
        _source = Transforms.Apply(_source!, t);
        var (w, h) = t is OrthoTransform.RotateLeft or OrthoTransform.RotateRight ? (_bounds.Height, _bounds.Width) : (_bounds.Width, _bounds.Height);
        _bounds = new Rect(Math.Round(center.X - (w / 2)), Math.Round(center.Y - (h / 2)), w, h);
        UpdateFloating();
        return true;
    }

    /// <summary>Resizes and/or skews only the selection.</summary>
    public bool ResizeSkewSelection(int width, int height, double skewH, double skewV)
    {
        if (!EnsureFloating("Resize selection"))
        {
            return false;
        }

        var src = Resampler.ResizeAuto(CurrentScaled(_sourceOriginal!), width, height);
        if (skewH != 0 || skewV != 0)
        {
            var (m, nw, nh) = Transforms.SkewGeometry(width, height, skewH, skewV);
            src = Transforms.Affine(src, m, nw, nh);
        }

        _sourceOriginal = src;
        _source = Host.Settings.TransparentSelection ? SelectionOperations.KeyOut(src, Host.Settings.Secondary) : src;
        _bounds = new Rect(_bounds.X, _bounds.Y, src.Width, src.Height);
        UpdateFloating();
        return true;
    }

    /// <summary>Inverts the colors of the selection only.</summary>
    public bool InvertSelectionColors()
    {
        if (!EnsureFloating("Invert colors"))
        {
            return false;
        }

        Transforms.Invert(_sourceOriginal!);
        Transforms.Invert(_source!);
        UpdateFloating();
        return true;
    }

    /// <summary>Re-applies the transparent-selection setting to the floating pixels.</summary>
    public void RefreshTransparency()
    {
        if (_sourceOriginal is null)
        {
            return;
        }

        _source = Host.Settings.TransparentSelection ? SelectionOperations.KeyOut(_sourceOriginal, Host.Settings.Secondary) : _sourceOriginal;
        UpdateFloating();
    }

    private bool EnsureFloating(string stepName)
    {
        if (!HasSelection)
        {
            return false;
        }

        if (!IsFloating)
        {
            Lift(clearSource: true, stepName);
        }

        return true;
    }

    private PixelBuffer CurrentScaled(PixelBuffer src)
    {
        var w = (int)_bounds.Width;
        var h = (int)_bounds.Height;
        return w == src.Width && h == src.Height ? src : Resampler.Resize(src, w, h, ResampleMode.NearestNeighbor);
    }

    private void Lift(bool clearSource, string stepName)
    {
        var doc = Host.Document;
        var region = _region!;
        _layer = doc.ActiveLayer;
        if (SelectionOperations.FlattenTextIn(doc, _layer, region.Bounds))
        {
            Host.TextFlattened();
        }

        _sourceOriginal = SelectionOperations.Extract(doc, _layer, region);
        _source = Host.Settings.TransparentSelection ? SelectionOperations.KeyOut(_sourceOriginal, Host.Settings.Secondary) : _sourceOriginal;
        if (clearSource)
        {
            SelectionOperations.Clear(doc, _layer, region, Host.Settings.Secondary);
        }

        _bounds = region.Bounds.ToRect();
        _region = null;
        _stepName = clearSource ? stepName : "Duplicate selection";
        UpdateFloating();
    }

    private void UpdateFloating()
    {
        if (_source is null)
        {
            return;
        }

        Host.Document.Floating = new FloatingImage(CurrentScaled(_source), (int)_bounds.X, (int)_bounds.Y);
        Host.ToolStateChanged();
    }

    private void StampFloating()
    {
        if (Host.Document.Floating is { } f && _layer is not null)
        {
            SelectionOperations.Stamp(Host.Document, _layer, f.Pixels, f.X, f.Y);
        }
    }

    private void CommitFloating(bool commit = true)
    {
        var doc = Host.Document;
        StampFloating();
        doc.Floating = null;
        _source = _sourceOriginal = null;
        if (commit)
        {
            doc.Commit(_stepName);
        }
        _stepName = "Move selection";
    }
}
