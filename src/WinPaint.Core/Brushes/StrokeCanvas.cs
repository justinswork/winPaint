using WinPaint.Core.Imaging;

namespace WinPaint.Core.Brushes;

/// <summary>How a stamp combines with what the stroke already painted.</summary>
public enum StrokeCombine
{
    /// <summary>Keep the more opaque pixel (no build-up within one stroke).</summary>
    Max,

    /// <summary>Source-over (builds up, e.g. airbrush).</summary>
    Over,
}

/// <summary>
/// The pixels of a single stroke in progress, kept separate from the layer so opacity and non-accumulating
/// brushes can be applied against the layer's pre-stroke pixels.
/// </summary>
public sealed class StrokeCanvas
{
    /// <summary>Creates a stroke canvas the size of the document.</summary>
    public StrokeCanvas(int width, int height)
    {
        Surface = new TiledSurface(width, height);
    }

    /// <summary>Premultiplied stroke pixels.</summary>
    public TiledSurface Surface { get; private set; }

    /// <summary>Region touched since the last <see cref="TakeDirty"/>.</summary>
    public PixelRect Dirty { get; private set; }

    /// <summary>Total region touched by the stroke.</summary>
    public PixelRect TotalBounds { get; private set; }

    /// <summary>Canvas width.</summary>
    public int Width => Surface.Width;

    /// <summary>Canvas height.</summary>
    public int Height => Surface.Height;

    /// <summary>Optional clip: when set, pixels outside are ignored.</summary>
    public Func<int, int, bool>? Clip { get; set; }

    /// <summary>Erases everything drawn so far (the cleared area is reported as dirty).</summary>
    public void Clear()
    {
        Dirty = Dirty.Union(TotalBounds);
        Surface = new TiledSurface(Surface.Width, Surface.Height);
    }

    /// <summary>Returns and clears the dirty region.</summary>
    public PixelRect TakeDirty()
    {
        var d = Dirty;
        Dirty = PixelRect.Empty;
        return d;
    }

    /// <summary>Plots a premultiplied pixel.</summary>
    public void Plot(int x, int y, uint premultiplied, StrokeCombine combine)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || premultiplied == 0)
        {
            return;
        }

        if (Clip is not null && !Clip(x, y))
        {
            return;
        }

        var data = Surface.GetWritableData(x >> Tile.Shift, y >> Tile.Shift);
        var i = ((y & (Tile.Size - 1)) << Tile.Shift) + (x & (Tile.Size - 1));
        var old = data[i];
        data[i] = combine == StrokeCombine.Over
            ? ColorUtil.Over(premultiplied, old)
            : (ColorUtil.A(premultiplied) > ColorUtil.A(old) ? premultiplied : old);
        Touch(new PixelRect(x, y, 1, 1));
    }

    /// <summary>Plots a straight color with a 0..1 coverage.</summary>
    public void PlotCoverage(int x, int y, System.Windows.Media.Color color, double coverage, StrokeCombine combine)
    {
        if (coverage <= 0)
        {
            return;
        }

        Plot(x, y, ColorUtil.FromColor(color, Math.Min(1, coverage)), combine);
    }

    /// <summary>Extends the dirty region.</summary>
    public void Touch(PixelRect r)
    {
        Dirty = Dirty.Union(r);
        TotalBounds = TotalBounds.Union(r);
    }
}
