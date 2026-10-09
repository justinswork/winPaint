using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;

namespace WinPaint.App.Controls;

/// <summary>Dirty-rectangle pipeline from the document composite into the displayed WriteableBitmap.</summary>
public sealed partial class CanvasView
{
    private WriteableBitmap? _bitmap;
    private PixelRect _dirty;
    private bool _renderHooked;
    private uint[] _scratch = [];

    /// <summary>The displayed bitmap (composite of the document).</summary>
    internal WriteableBitmap? Bitmap => _bitmap;

    /// <summary>Synchronously flushes pending repaints (used before screenshots/tests).</summary>
    public void FlushRendering()
    {
        if (_doc is not null && !_dirty.IsEmpty)
        {
            RenderDirty();
        }
    }

    private void AttachDocument(PaintDocument? doc)
    {
        if (_doc is not null)
        {
            _doc.Invalidated -= OnDocumentInvalidated;
            _doc.StructureChanged -= OnStructureChanged;
        }

        _doc = doc;
        if (_doc is null)
        {
            _image.Source = null;
            _bitmap = null;
            return;
        }

        _doc.Invalidated += OnDocumentInvalidated;
        _doc.StructureChanged += OnStructureChanged;
        EnsureBitmap();
        _dirty = _doc.Bounds;
        HookRendering();
        UpdateView();
    }

    private void OnStructureChanged(object? sender, EventArgs e)
    {
        if (_doc is null)
        {
            return;
        }

        if (_bitmap is null || _bitmap.PixelWidth != _doc.Width || _bitmap.PixelHeight != _doc.Height)
        {
            EnsureBitmap();
            _dirty = _doc.Bounds;
            HookRendering();
            UpdateView();
        }

        _overlay.InvalidateVisual();
        _editor.Sync();
    }

    private void EnsureBitmap()
    {
        if (_doc is null)
        {
            return;
        }

        if (_bitmap is null || _bitmap.PixelWidth != _doc.Width || _bitmap.PixelHeight != _doc.Height)
        {
            _bitmap = new WriteableBitmap(_doc.Width, _doc.Height, 96, 96, PixelFormats.Pbgra32, null);
            _image.Source = _bitmap;
            _dirty = _doc.Bounds;
        }
    }

    private void OnDocumentInvalidated(object? sender, PixelRect r)
    {
        _dirty = _dirty.Union(r);
        HookRendering();
    }

    private void HookRendering()
    {
        if (!_renderHooked)
        {
            _renderHooked = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void StopRendering()
    {
        if (_renderHooked)
        {
            _renderHooked = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        StopRendering();
        RenderDirty();
    }

    private void RenderDirty()
    {
        if (_doc is null)
        {
            return;
        }

        EnsureBitmap();
        var r = _dirty.Intersect(_doc.Bounds);
        _dirty = PixelRect.Empty;
        if (r.IsEmpty || _bitmap is null)
        {
            return;
        }

        var n = r.Width * r.Height;
        if (_scratch.Length < n || _scratch.Length > n * 4)
        {
            _scratch = new uint[n];
        }

        _doc.RenderComposite(r, _scratch, 0, r.Width);
        _bitmap.WritePixels(new Int32Rect(r.X, r.Y, r.Width, r.Height), _scratch, r.Width * 4, 0);
    }
}
