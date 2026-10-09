using WinPaint.Core.Imaging;

namespace WinPaint.Core.Document;

/// <summary>An entry in a layer's element stack: a <see cref="RasterSegment"/> or a <see cref="TextObject"/>.</summary>
public abstract class LayerElement
{
    /// <summary>Stable identity of the element (kept across undo/redo).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Deep copy that keeps the same <see cref="Id"/>. Pixel surfaces are shared copy-on-write.</summary>
    public abstract LayerElement CloneElement();
}

/// <summary>
/// A block of painted pixels in a layer's element stack. Pixels are composited over everything below after
/// the (optional) <see cref="Erase"/> mask has removed coverage from everything below.
/// </summary>
public sealed class RasterSegment : LayerElement
{
    /// <summary>Creates an empty segment.</summary>
    public RasterSegment(int width, int height)
    {
        Pixels = new TiledSurface(width, height);
    }

    /// <summary>Creates a segment from existing surfaces.</summary>
    public RasterSegment(TiledSurface pixels, TiledSurface? erase)
    {
        Pixels = pixels;
        Erase = erase;
    }

    /// <summary>Premultiplied pixels painted in this segment.</summary>
    public TiledSurface Pixels { get; set; }

    /// <summary>
    /// Transparent-erase mask applied to everything below this segment (value 0..255 in the low byte of each
    /// pixel; 255 = fully erased). Null when nothing was erased.
    /// </summary>
    public TiledSurface? Erase { get; set; }

    /// <summary>True when the segment holds neither pixels nor erasure.</summary>
    public bool IsEmpty => Pixels.IsEmpty && (Erase is null || Erase.IsEmpty);

    /// <summary>Returns the erase mask, creating it on demand.</summary>
    public TiledSurface EnsureErase() => Erase ??= new TiledSurface(Pixels.Width, Pixels.Height);

    /// <inheritdoc/>
    public override LayerElement CloneElement() => new RasterSegment(Pixels.Snapshot(), Erase?.Snapshot()) { Id = Id };

    /// <summary>Copy with a fresh identity (for layer duplication).</summary>
    public RasterSegment CloneWithNewId() => new(Pixels.Snapshot(), Erase?.Snapshot());
}
