using System.Windows.Media;
using WinPaint.Core.Brushes;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Tools;

/// <summary>How a stroke's pixels are applied to the layer.</summary>
public enum StrokeMode
{
    /// <summary>Stroke pixels (× opacity) over the pre-stroke pixels.</summary>
    Paint,

    /// <summary>Stroke coverage erases to transparent (also below the segment via its erase mask).</summary>
    EraseTransparent,

    /// <summary>Under the stroke, pixels equal to <c>ReplaceFrom</c> become <c>ReplaceTo</c>.</summary>
    ColorReplace,
}

/// <summary>
/// One stroke in progress on the active layer's top segment. Keeps the pre-stroke pixels so opacity and
/// non-accumulating brushes are computed against them, and commits a single undo step.
/// </summary>
public sealed class StrokeSession
{
    private readonly PaintDocument _doc;
    private readonly Layer _layer;
    private readonly RasterSegment _segment;
    private readonly TiledSurface _before;
    private readonly TiledSurface? _beforeErase;
    private readonly bool _useEraseMask;
    private readonly int _opacity;
    private readonly PixelBuffer? _replaceComposite;
    private readonly uint _replaceFrom;
    private readonly uint _replaceTo;

    /// <summary>Starts a stroke.</summary>
    public StrokeSession(PaintDocument doc, StrokeMode mode, double opacity, Color? replaceFrom = null, Color? replaceTo = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        _doc = doc;
        _layer = doc.ActiveLayer;
        _segment = _layer.TopSegment;
        _before = _segment.Pixels.Snapshot();
        _beforeErase = _segment.Erase?.Snapshot();
        _useEraseMask = _layer.Elements.Count > 1;
        Mode = mode;
        _opacity = (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        Canvas = new StrokeCanvas(doc.Width, doc.Height);
        if (mode == StrokeMode.ColorReplace)
        {
            _replaceComposite = doc.LayerComposite(_layer);
            _replaceFrom = ColorUtil.FromColor(replaceFrom ?? Colors.Black);
            _replaceTo = ColorUtil.FromColor(replaceTo ?? Colors.White);
        }
    }

    /// <summary>Stroke mode.</summary>
    public StrokeMode Mode { get; }

    /// <summary>The stroke's own pixels.</summary>
    public StrokeCanvas Canvas { get; }

    /// <summary>Applies the newly stamped region to the layer and repaints it.</summary>
    public void Flush()
    {
        var r = Canvas.TakeDirty().Intersect(_doc.Bounds);
        if (r.IsEmpty)
        {
            return;
        }

        var n = r.Width * r.Height;
        var stroke = new uint[n];
        var before = new uint[n];
        Canvas.Surface.ReadRect(r, stroke, 0, r.Width);
        _before.ReadRect(r, before, 0, r.Width);
        switch (Mode)
        {
            case StrokeMode.Paint:
                for (var i = 0; i < n; i++)
                {
                    var s = stroke[i];
                    if (s != 0)
                    {
                        before[i] = ColorUtil.Over(ColorUtil.Scale(s, _opacity), before[i]);
                    }
                }

                break;
            case StrokeMode.ColorReplace:
                for (var y = 0; y < r.Height; y++)
                {
                    for (var x = 0; x < r.Width; x++)
                    {
                        var i = (y * r.Width) + x;
                        if (stroke[i] != 0 && _replaceComposite![r.X + x, r.Y + y] == _replaceFrom)
                        {
                            before[i] = _replaceTo;
                        }
                    }
                }

                break;
            case StrokeMode.EraseTransparent:
                var erase = _useEraseMask ? new uint[n] : null;
                _beforeErase?.ReadRect(r, erase!, 0, r.Width);
                for (var i = 0; i < n; i++)
                {
                    var m = (int)ColorUtil.A(stroke[i]);
                    if (m == 0)
                    {
                        continue;
                    }

                    before[i] = ColorUtil.Scale(before[i], 255 - m);
                    if (erase is not null)
                    {
                        var e = (int)(erase[i] & 0xFF);
                        erase[i] = (uint)(255 - ColorUtil.Mul(255 - e, 255 - m));
                    }
                }

                if (erase is not null)
                {
                    _segment.EnsureErase().WriteRect(r, erase, 0, r.Width);
                }

                break;
        }

        _segment.Pixels.WriteRect(r, before, 0, r.Width);
        _doc.Invalidate(_layer, r);
    }

    /// <summary>Applies remaining changes and records one undo step (nothing is recorded for an empty stroke).</summary>
    public void Commit(string name)
    {
        Flush();
        if (Canvas.TotalBounds.IsEmpty)
        {
            return;
        }

        _doc.Commit(name);
    }

    /// <summary>Restores the pre-stroke pixels.</summary>
    public void Cancel()
    {
        _segment.Pixels = _before.Snapshot();
        _segment.Erase = _beforeErase?.Snapshot();
        _doc.Invalidate(_layer, Canvas.TotalBounds);
    }
}
