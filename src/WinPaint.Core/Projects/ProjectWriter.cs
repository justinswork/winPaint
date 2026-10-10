using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WinPaint.Core.Document;
using WinPaint.Core.History;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Projects;

/// <summary>Options for writing a project container.</summary>
public sealed record ProjectWriteOptions
{
    /// <summary>
    /// Flattened image for <c>preview.png</c> and <c>thumbnail.png</c> (standalone .wpp only; null when embedding).
    /// </summary>
    public PixelBuffer? Preview { get; init; }

    /// <summary>Fingerprint of the host image (embedded containers only).</summary>
    public string? HostFingerprint { get; init; }

    /// <summary>
    /// True for extension ids whose plugin is installed. Data of other extensions with the
    /// <see cref="ExtensionPolicy.Discard"/> policy is left out. Null = nothing is installed.
    /// </summary>
    public Func<string, bool>? IsExtensionInstalled { get; init; }

    /// <summary>Version written to <c>manifest.generator</c>.</summary>
    public string GeneratorVersion { get; init; } = "1.0.0";
}

/// <summary>Writes the project container: a ZIP archive described in docs/specs/wpp-format.md §3.</summary>
public static class ProjectWriter
{
    /// <summary>Longest side of <c>thumbnail.png</c>.</summary>
    public const int ThumbnailSize = 256;

    /// <summary>Writes a container to a byte array.</summary>
    public static byte[] WriteToBytes(DocumentState state, ProjectWriteOptions? options = null)
    {
        using var ms = new MemoryStream();
        Write(state, ms, options);
        return ms.ToArray();
    }

    /// <summary>Writes a container to a stream.</summary>
    public static void Write(DocumentState state, Stream stream, ProjectWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new ProjectWriteOptions();

        var parts = new List<Part>();
        var document = BuildDocument(state, parts);
        parts.Insert(0, new Part("document.json", "application/json", JsonSerializer.SerializeToUtf8Bytes(document, ProjectJson.Options), true));

        if (options.Preview is { } preview)
        {
            parts.Add(new Part("preview.png", "image/png", ProjectImages.EncodeColorPng(preview), false));
            parts.Add(new Part("thumbnail.png", "image/png", ProjectImages.EncodeColorPng(Thumbnail(preview)), false));
        }

        var installed = options.IsExtensionInstalled ?? (_ => false);
        var extensions = state.Extensions.Where(e => e.Policy == ExtensionPolicy.Keep || installed(e.Id));
        foreach (var ext in extensions.Extensions)
        {
            foreach (var (path, data) in ext.Files)
            {
                parts.Add(new Part($"extensions/{ext.Id}/{path}", "application/octet-stream", data, true));
            }
        }

        var manifest = new ManifestDto
        {
            Format = ProjectFormat.FormatName,
            FormatVersion = $"{ProjectFormat.MajorVersion}.{ProjectFormat.MinorVersion}",
            Generator = new GeneratorDto { Name = "winPaint", Version = options.GeneratorVersion },
            Parts = [.. parts.Select(p => new PartDto { Path = p.Path, MediaType = p.MediaType, Sha256 = ProjectImages.Sha256(p.Data) })],
            HostFingerprint = options.HostFingerprint is null ? null : new FingerprintDto { Algorithm = ProjectFormat.FingerprintAlgorithm, Value = options.HostFingerprint },
            Extensions = [.. extensions.Extensions.Select(e => new ExtensionDto
            {
                Id = e.Id,
                DisplayName = e.DisplayName,
                Version = e.Version,
                DataVersion = e.DataVersion,
                InfoUrl = e.InfoUrl,
                Policy = e.Policy == ExtensionPolicy.Discard ? "discard" : "keep",
            })],
            Anchors = [.. extensions.Anchors.Select(ToDto)],
        };

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        WriteEntry(zip, "mimetype", Encoding.ASCII.GetBytes(ProjectFormat.MediaType), compress: false);
        WriteEntry(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, ProjectJson.Options), compress: true);
        foreach (var p in parts)
        {
            WriteEntry(zip, p.Path, p.Data, p.Compress);
        }
    }

    private static DocumentDto BuildDocument(DocumentState state, List<Part> parts)
    {
        var layers = new List<LayerDto>();
        foreach (var layer in state.Layers)
        {
            var elements = new List<ElementDto>();
            foreach (var e in layer.Elements)
            {
                switch (e)
                {
                    case RasterSegment s:
                        elements.Add(new RasterDto
                        {
                            Id = ProjectFormat.IdString(s.Id),
                            Pixels = SurfaceDto(s.Pixels, ProjectFormat.PixelsPart(layer, s), mask: false, parts),
                            Erase = s.Erase is null ? null : SurfaceDto(s.Erase, ProjectFormat.ErasePart(layer, s), mask: true, parts),
                        });
                        break;
                    case TextObject t:
                        elements.Add(new TextDto
                        {
                            Id = ProjectFormat.IdString(t.Id),
                            Text = t.Text,
                            Box = [t.Box.X, t.Box.Y, t.Box.Width, t.Box.Height],
                            Transform = [t.Transform.M11, t.Transform.M12, t.Transform.M21, t.Transform.M22, t.Transform.OffsetX, t.Transform.OffsetY],
                            FontFamily = t.FontFamily,
                            FontSizePt = t.FontSizePt,
                            Bold = t.Bold,
                            Italic = t.Italic,
                            Underline = t.Underline,
                            Strikethrough = t.Strikethrough,
                            Foreground = ProjectImages.ToHex(t.Foreground),
                            Background = ProjectImages.ToHex(t.Background),
                            OpaqueBackground = t.OpaqueBackground,
                            Opacity = t.Opacity,
                        });
                        break;
                }
            }

            layers.Add(new LayerDto
            {
                Id = ProjectFormat.IdString(layer.Id),
                Name = layer.Name,
                Visible = layer.Visible,
                Opacity = layer.Opacity,
                BlendMode = layer.BlendMode.ToString().ToLowerInvariant(),
                IsBackground = layer.IsBackground,
                IsTransparent = layer.IsTransparent,
                Elements = elements,
            });
        }

        var active = state.Layers[Math.Clamp(state.ActiveLayerIndex, 0, state.Layers.Count - 1)];
        return new DocumentDto
        {
            Canvas = new CanvasDto { Width = state.Width, Height = state.Height, DpiX = state.DpiX, DpiY = state.DpiY },
            ActiveLayerId = ProjectFormat.IdString(active.Id),
            Layers = layers,
        };
    }

    private static PixelsDto? SurfaceDto(TiledSurface surface, string path, bool mask, List<Part> parts)
    {
        if (surface.TryGetUniform(out var value))
        {
            if (value == 0)
            {
                return null;
            }

            if (!mask)
            {
                return new PixelsDto { Fill = ProjectImages.ToHex(value) };
            }
        }

        var bounds = surface.ContentBounds();
        if (bounds.IsEmpty)
        {
            return null;
        }

        var pixels = surface.ToPixelBuffer(bounds);
        var png = mask ? ProjectImages.EncodeMaskPng(pixels) : ProjectImages.EncodeColorPng(pixels);
        parts.Add(new Part(path, "image/png", png, false));
        return new PixelsDto { Part = path, X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height };
    }

    private static AnchorDto ToDto(Anchor a)
    {
        var target = new TargetDto { Kind = a.Kind.ToString().ToLowerInvariant() };
        switch (a.Kind)
        {
            case AnchorKind.Layer:
                target.LayerId = a.LayerId is { } l ? ProjectFormat.IdString(l) : null;
                break;
            case AnchorKind.Text:
                target.TextId = a.TextId is { } t ? ProjectFormat.IdString(t) : null;
                break;
            case AnchorKind.Region when a.Region is { } region:
                target.LayerId = a.LayerId is { } rl ? ProjectFormat.IdString(rl) : null;
                target.Shape = region.Rect is { } r
                    ? new ShapeDto { Type = "rect", Rect = [r.X, r.Y, r.Width, r.Height] }
                    : new ShapeDto { Type = "polygon", Points = [.. region.Points.Select(p => new[] { p.X, p.Y })] };
                break;
        }

        return new AnchorDto { Id = a.Id, Extension = a.ExtensionId, Target = target };
    }

    private static PixelBuffer Thumbnail(PixelBuffer image)
    {
        var longest = Math.Max(image.Width, image.Height);
        if (longest <= ThumbnailSize)
        {
            return image;
        }

        var scale = (double)ThumbnailSize / longest;
        return Resampler.ResizeAuto(image, Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
    }

    private static void WriteEntry(ZipArchive zip, string path, byte[] data, bool compress)
    {
        var entry = zip.CreateEntry(path, compress ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var s = entry.Open();
        s.Write(data);
    }

    private sealed record Part(string Path, string MediaType, byte[] Data, bool Compress);
}
