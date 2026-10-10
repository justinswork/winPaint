using System.Text.RegularExpressions;
using WinPaint.Core.Document;
using WinPaint.Core.History;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Projects;

/// <summary>Thrown when a project container is invalid, unsupported or exceeds a limit.</summary>
public sealed class ProjectFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ProjectFormatException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public ProjectFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ProjectFormatException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>True when the container was written by a newer, incompatible major version.</summary>
    public bool IsNewerMajorVersion { get; init; }
}

/// <summary>Constants, limits and validation rules of the winPaint project format (docs/specs/wpp-format.md).</summary>
public static partial class ProjectFormat
{
    /// <summary>Content of the <c>mimetype</c> entry.</summary>
    public const string MediaType = "application/vnd.winpaint.project+zip";

    /// <summary>Value of <c>manifest.format</c>.</summary>
    public const string FormatName = "winpaint-project";

    /// <summary>Major format version written and understood.</summary>
    public const int MajorVersion = 1;

    /// <summary>Minor format version written.</summary>
    public const int MinorVersion = 0;

    /// <summary>Standalone project file extension.</summary>
    public const string FileExtension = ".wpp";

    /// <summary>Fingerprint algorithm name.</summary>
    public const string FingerprintAlgorithm = "sha256-rgba8";

    /// <summary>Maximum canvas edge.</summary>
    public const int MaxCanvasEdge = 100_000;

    /// <summary>Maximum canvas pixel count.</summary>
    public const long MaxCanvasPixels = 1_500_000_000;

    /// <summary>Maximum number of container entries.</summary>
    public const int MaxEntries = 100_000;

    /// <summary>Maximum total uncompressed size.</summary>
    public const long MaxUncompressedTotal = 8L << 30;

    /// <summary>Maximum ratio of uncompressed to compressed size (zip-bomb guard).</summary>
    public const long MaxCompressionRatio = 200;

    /// <summary>Maximum <c>manifest.json</c> size.</summary>
    public const int MaxManifestBytes = 16 << 20;

    /// <summary>Maximum <c>document.json</c> size.</summary>
    public const int MaxDocumentBytes = 256 << 20;

    /// <summary>Maximum JSON nesting depth.</summary>
    public const int MaxJsonDepth = 64;

    /// <summary>Maximum characters in one text object.</summary>
    public const int MaxTextLength = 1_000_000;

    /// <summary>Maximum data per extension.</summary>
    public const long MaxExtensionBytes = 256L << 20;

    /// <summary>Maximum embedded payload (header + container).</summary>
    public const long MaxEmbeddedPayload = int.MaxValue;

    /// <summary>
    /// True for a valid extension id: reverse-DNS, lowercase letters, digits, '.' and '-', 3–128 characters.
    /// </summary>
    public static bool IsValidExtensionId(string? id) => id is not null && ExtensionIdRegex().IsMatch(id);

    /// <summary>
    /// True for a valid relative part path: '/'-separated, no empty, '.' or '..' segments, no leading '/', no
    /// backslash, colon or control characters.
    /// </summary>
    public static bool IsValidPartPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1024 || path[0] == '/' || path[^1] == '/')
        {
            return false;
        }

        foreach (var c in path)
        {
            if (c is '\\' or ':' || char.IsControl(c))
            {
                return false;
            }
        }

        foreach (var seg in path.Split('/'))
        {
            if (seg.Length == 0 || seg == "." || seg == "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when the document holds something a plain image would lose (WPP spec §7.1): live text, more than one
    /// layer, a layer with non-default opacity, blend mode or visibility, or extension data.
    /// </summary>
    public static bool IsWorthKeeping(DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Layers.Count > 1
            || !state.Extensions.IsEmpty
            || state.Layers.Any(l => !l.Visible || l.Opacity < 1 || l.BlendMode != BlendMode.Normal || l.TextObjects.Any());
    }

    /// <summary>Number of hidden layers.</summary>
    public static int HiddenLayerCount(DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Layers.Count(l => !l.Visible);
    }

    /// <summary>Canvas size check.</summary>
    internal static bool IsValidCanvas(int width, int height) =>
        width is > 0 and <= MaxCanvasEdge && height is > 0 and <= MaxCanvasEdge && (long)width * height <= MaxCanvasPixels;

    /// <summary>Lowercase GUID without braces.</summary>
    internal static string IdString(Guid id) => id.ToString("D");

    /// <summary>Part path of a segment's pixels.</summary>
    internal static string PixelsPart(Layer layer, RasterSegment s) => $"layers/{IdString(layer.Id)}/{IdString(s.Id)}.png";

    /// <summary>Part path of a segment's erase mask.</summary>
    internal static string ErasePart(Layer layer, RasterSegment s) => $"layers/{IdString(layer.Id)}/{IdString(s.Id)}.erase.png";

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{1,126})[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionIdRegex();
}
