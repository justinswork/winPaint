using System.Collections.Immutable;
using System.Windows;
using System.Windows.Media;

namespace WinPaint.Core.Projects;

/// <summary>What a winPaint without the extension does with its data when saving (WPP spec §5.2).</summary>
public enum ExtensionPolicy
{
    /// <summary>Carry the data along unchanged.</summary>
    Keep,

    /// <summary>Leave the data out when the document is saved.</summary>
    Discard,
}

/// <summary>One extension's registry entry and files (WPP spec §5). Immutable; shared by history snapshots.</summary>
public sealed record ExtensionData
{
    /// <summary>Reverse-DNS extension id.</summary>
    public required string Id { get; init; }

    /// <summary>Name shown to users.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Version of the plugin that wrote the data (informational).</summary>
    public string? Version { get; init; }

    /// <summary>The plugin's own data version (never interpreted by winPaint).</summary>
    public string? DataVersion { get; init; }

    /// <summary>Where users can get the plugin.</summary>
    public string? InfoUrl { get; init; }

    /// <summary>Behavior when the plugin is missing.</summary>
    public ExtensionPolicy Policy { get; init; }

    /// <summary>Files relative to the extension's folder.</summary>
    public ImmutableSortedDictionary<string, byte[]> Files { get; init; } = ImmutableSortedDictionary.Create<string, byte[]>(StringComparer.Ordinal);

    /// <summary>Total size of the files in bytes.</summary>
    public long DataSize => Files.Values.Sum(f => (long)f.Length);
}

/// <summary>The kind of part an anchor attaches to (WPP spec §5.3).</summary>
public enum AnchorKind
{
    /// <summary>The whole project.</summary>
    Document,

    /// <summary>One layer.</summary>
    Layer,

    /// <summary>One live text object.</summary>
    Text,

    /// <summary>An area of the canvas.</summary>
    Region,
}

/// <summary>A region anchor's shape: an axis-aligned rectangle or a polygon, in canvas pixels.</summary>
public sealed record RegionShape
{
    private RegionShape(Rect? rect, ImmutableArray<Point> points)
    {
        Rect = rect;
        Points = points;
    }

    /// <summary>The rectangle, or null for a polygon.</summary>
    public Rect? Rect { get; }

    /// <summary>The polygon's points (the rectangle's corners for a rectangle).</summary>
    public ImmutableArray<Point> Points { get; }

    /// <summary>Bounding box.</summary>
    public Rect Bounds
    {
        get
        {
            if (Rect is { } r)
            {
                return r;
            }

            var b = System.Windows.Rect.Empty;
            foreach (var p in Points)
            {
                b.Union(p);
            }

            return b;
        }
    }

    /// <summary>Creates a rectangle shape.</summary>
    public static RegionShape FromRect(Rect r) =>
        new(r, [r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft]);

    /// <summary>Creates a polygon shape.</summary>
    public static RegionShape FromPolygon(IEnumerable<Point> points) => new(null, [.. points]);

    /// <summary>Applies a matrix. Rectangles stay rectangles when the result is still axis-aligned.</summary>
    public RegionShape Transform(Matrix m)
    {
        var pts = Points.Select(m.Transform).ToArray();
        if (Rect is not null && pts.Length == 4 && IsAxisAligned(pts))
        {
            var b = System.Windows.Rect.Empty;
            foreach (var p in pts)
            {
                b.Union(p);
            }

            return FromRect(b);
        }

        return FromPolygon(pts);
    }

    private static bool IsAxisAligned(Point[] p)
    {
        const double eps = 1e-9;
        for (var i = 0; i < 4; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % 4];
            if (Math.Abs(a.X - b.X) > eps && Math.Abs(a.Y - b.Y) > eps)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Attaches an extension's data to part of the document (WPP spec §5.3).</summary>
/// <param name="Id">Anchor id, unique in the document.</param>
/// <param name="ExtensionId">Owning extension.</param>
/// <param name="Kind">Target kind.</param>
/// <param name="LayerId">Target layer (<see cref="AnchorKind.Layer"/>) or optional layer of a region.</param>
/// <param name="TextId">Target text object (<see cref="AnchorKind.Text"/>).</param>
/// <param name="Region">Target area (<see cref="AnchorKind.Region"/>).</param>
public sealed record Anchor(string Id, string ExtensionId, AnchorKind Kind, Guid? LayerId = null, Guid? TextId = null, RegionShape? Region = null);

/// <summary>All extension data of a document. Immutable, so history snapshots share it.</summary>
/// <param name="Extensions">Registry entries with their files.</param>
/// <param name="Anchors">Anchors of all extensions.</param>
public sealed record ProjectExtensions(ImmutableArray<ExtensionData> Extensions, ImmutableArray<Anchor> Anchors)
{
    /// <summary>No extension data.</summary>
    public static ProjectExtensions Empty { get; } = new([], []);

    /// <summary>True when there is no extension data.</summary>
    public bool IsEmpty => Extensions.IsEmpty && Anchors.IsEmpty;

    /// <summary>Removes an extension and its anchors.</summary>
    public ProjectExtensions Remove(string extensionId) => new(
        [.. Extensions.Where(e => e.Id != extensionId)],
        [.. Anchors.Where(a => a.ExtensionId != extensionId)]);

    /// <summary>Keeps only the extensions matching <paramref name="keep"/> (and their anchors).</summary>
    public ProjectExtensions Where(Func<ExtensionData, bool> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        var kept = Extensions.Where(keep).ToImmutableArray();
        var ids = kept.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        return new(kept, [.. Anchors.Where(a => ids.Contains(a.ExtensionId))]);
    }
}
