using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinPaint.Core.Projects;

// JSON shapes of manifest.json and document.json (WPP spec §3.2–3.3, §5). Internal; mapped to and from the document
// model by ProjectWriter and ProjectReader. Unknown properties are ignored on read and never written back.
#pragma warning disable CA1812 // instantiated by System.Text.Json
#pragma warning disable CA1819 // arrays mirror JSON arrays

internal static class ProjectJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = ProjectFormat.MaxJsonDepth,
        AllowOutOfOrderMetadataProperties = true,
        NumberHandling = JsonNumberHandling.Strict,
        WriteIndented = true,
    };
}

internal sealed class ManifestDto
{
    public string? Format { get; set; }

    public string? FormatVersion { get; set; }

    public GeneratorDto? Generator { get; set; }

    public List<PartDto>? Parts { get; set; }

    public FingerprintDto? HostFingerprint { get; set; }

    public List<ExtensionDto>? Extensions { get; set; }

    public List<AnchorDto>? Anchors { get; set; }
}

internal sealed class GeneratorDto
{
    public string? Name { get; set; }

    public string? Version { get; set; }
}

internal sealed class PartDto
{
    public string? Path { get; set; }

    public string? MediaType { get; set; }

    public string? Sha256 { get; set; }
}

internal sealed class FingerprintDto
{
    public string? Algorithm { get; set; }

    public string? Value { get; set; }
}

internal sealed class ExtensionDto
{
    public string? Id { get; set; }

    public string? DisplayName { get; set; }

    public string? Version { get; set; }

    public string? DataVersion { get; set; }

    public string? InfoUrl { get; set; }

    public string? Policy { get; set; }
}

internal sealed class AnchorDto
{
    public string? Id { get; set; }

    public string? Extension { get; set; }

    public TargetDto? Target { get; set; }
}

internal sealed class TargetDto
{
    public string? Kind { get; set; }

    public string? LayerId { get; set; }

    public string? TextId { get; set; }

    public ShapeDto? Shape { get; set; }
}

internal sealed class ShapeDto
{
    public string? Type { get; set; }

    public double[]? Rect { get; set; }

    public double[][]? Points { get; set; }
}

internal sealed class DocumentDto
{
    public CanvasDto? Canvas { get; set; }

    public string? ActiveLayerId { get; set; }

    public List<LayerDto>? Layers { get; set; }
}

internal sealed class CanvasDto
{
    public int Width { get; set; }

    public int Height { get; set; }

    public double DpiX { get; set; } = 96;

    public double DpiY { get; set; } = 96;
}

internal sealed class LayerDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public bool Visible { get; set; } = true;

    public double Opacity { get; set; } = 1;

    public string? BlendMode { get; set; }

    public bool IsBackground { get; set; }

    public bool IsTransparent { get; set; } = true;

    public List<ElementDto>? Elements { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RasterDto), "raster")]
[JsonDerivedType(typeof(TextDto), "text")]
internal abstract class ElementDto
{
    public string? Id { get; set; }
}

internal sealed class RasterDto : ElementDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PixelsDto? Pixels { get; set; }

    public PixelsDto? Erase { get; set; }
}

internal sealed class PixelsDto
{
    public string? Part { get; set; }

    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? Fill { get; set; }
}

internal sealed class TextDto : ElementDto
{
    public string? Text { get; set; }

    public double[]? Box { get; set; }

    public double[]? Transform { get; set; }

    public string? FontFamily { get; set; }

    public double FontSizePt { get; set; } = 11;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public bool Underline { get; set; }

    public bool Strikethrough { get; set; }

    public string? Foreground { get; set; }

    public string? Background { get; set; }

    public bool OpaqueBackground { get; set; }

    public double Opacity { get; set; } = 1;
}
