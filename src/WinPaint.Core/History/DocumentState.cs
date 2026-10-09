using WinPaint.Core.Document;

namespace WinPaint.Core.History;

/// <summary>
/// An immutable snapshot of a document's content (layers, element stacks, canvas size). Pixel tiles are shared
/// copy-on-write with the live document, so capturing a state is cheap.
/// </summary>
public sealed class DocumentState
{
    internal DocumentState(int width, int height, double dpiX, double dpiY, IReadOnlyList<Layer> layers, int activeLayerIndex)
    {
        Width = width;
        Height = height;
        DpiX = dpiX;
        DpiY = dpiY;
        Layers = layers;
        ActiveLayerIndex = activeLayerIndex;
    }

    /// <summary>Canvas width.</summary>
    public int Width { get; }

    /// <summary>Canvas height.</summary>
    public int Height { get; }

    /// <summary>Horizontal resolution.</summary>
    public double DpiX { get; }

    /// <summary>Vertical resolution.</summary>
    public double DpiY { get; }

    /// <summary>Frozen layer copies (never mutated; restore clones them again).</summary>
    public IReadOnlyList<Layer> Layers { get; }

    /// <summary>Active layer index at capture time.</summary>
    public int ActiveLayerIndex { get; }

    /// <summary>All pixel tiles referenced by the state.</summary>
    internal IEnumerable<Imaging.Tile> Tiles =>
        Layers.SelectMany(l => l.Elements.OfType<RasterSegment>())
              .SelectMany(s => s.Erase is null ? s.Pixels.AllocatedTiles : s.Pixels.AllocatedTiles.Concat(s.Erase.AllocatedTiles));

    /// <summary>Number of text objects in the state.</summary>
    internal int TextCount => Layers.Sum(l => l.TextObjects.Count());
}
