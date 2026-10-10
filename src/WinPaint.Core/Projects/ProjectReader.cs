using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using WinPaint.Core.Document;
using WinPaint.Core.History;
using WinPaint.Core.Imaging;

namespace WinPaint.Core.Projects;

/// <summary>A project read from a container.</summary>
/// <param name="State">The document content.</param>
/// <param name="HostFingerprint">Fingerprint of the host image (embedded containers), or null.</param>
/// <param name="IsNewerMinorVersion">Written by a newer winPaint; some information may be lost when saving.</param>
/// <param name="DamagedParts">Parts that were missing or failed their hash and were left out.</param>
public sealed record ProjectReadResult(DocumentState State, string? HostFingerprint, bool IsNewerMinorVersion, int DamagedParts);

/// <summary>
/// Reads a project container (docs/specs/wpp-format.md). Every limit in §9 is enforced; anything invalid throws
/// <see cref="ProjectFormatException"/>. Nothing is extracted to disk and nothing is executed.
/// </summary>
public static class ProjectReader
{
    /// <summary>Reads a container from bytes.</summary>
    public static ProjectReadResult Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var ms = new MemoryStream(data, writable: false);
        return Read(ms);
    }

    /// <summary>Reads a container from a seekable stream.</summary>
    public static ProjectReadResult Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return new Reader(stream).Read();
        }
        catch (ProjectFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FormatException or NotSupportedException
            or FileFormatException or OverflowException or ArgumentException or IOException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            throw new ProjectFormatException("The project is damaged or isn't a winPaint project.", ex);
        }
        catch (OutOfMemoryException ex)
        {
            throw new ProjectFormatException("There is not enough memory to open this project.", ex);
        }
    }

    private sealed class Reader(Stream stream)
    {
        private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _hashes = new(StringComparer.Ordinal);
        private long _budget;
        private int _damaged;

        public ProjectReadResult Read()
        {
            _budget = Math.Min(ProjectFormat.MaxUncompressedTotal, Math.Max(stream.Length, 1) * ProjectFormat.MaxCompressionRatio);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true, Encoding.UTF8);
            if (zip.Entries.Count == 0 || zip.Entries.Count > ProjectFormat.MaxEntries)
            {
                throw new ProjectFormatException("The project has too many or no entries.");
            }

            foreach (var e in zip.Entries)
            {
                if (!ProjectFormat.IsValidPartPath(e.FullName) || !_entries.TryAdd(e.FullName, e))
                {
                    throw new ProjectFormatException($"The project contains an invalid entry \"{e.FullName}\".");
                }
            }

            if (zip.Entries[0].FullName != "mimetype"
                || Encoding.ASCII.GetString(ReadEntry(zip.Entries[0], 256)) != ProjectFormat.MediaType)
            {
                throw new ProjectFormatException("This isn't a winPaint project.");
            }

            var manifest = Parse<ManifestDto>(Required("manifest.json", ProjectFormat.MaxManifestBytes, verify: false));
            if (manifest.Format != ProjectFormat.FormatName)
            {
                throw new ProjectFormatException("This isn't a winPaint project.");
            }

            var (major, minor) = ParseVersion(manifest.FormatVersion);
            if (major != ProjectFormat.MajorVersion)
            {
                throw new ProjectFormatException(
                    major > ProjectFormat.MajorVersion ? "This project was saved by a newer version of winPaint." : "This project's format version isn't supported.")
                {
                    IsNewerMajorVersion = major > ProjectFormat.MajorVersion,
                };
            }

            foreach (var p in manifest.Parts ?? [])
            {
                if (p.Path is not null && p.Sha256 is not null)
                {
                    _hashes[p.Path] = p.Sha256.ToLowerInvariant();
                }
            }

            var document = Parse<DocumentDto>(Required("document.json", ProjectFormat.MaxDocumentBytes, verify: true));
            var (layers, activeIndex, width, height, dpiX, dpiY) = BuildLayers(document);
            var extensions = ReadExtensions(manifest, layers);
            string? fingerprint = null;
            if (manifest.HostFingerprint is { Algorithm: ProjectFormat.FingerprintAlgorithm, Value: { } fp })
            {
                fingerprint = fp.ToLowerInvariant();
            }

            var state = new DocumentState(width, height, dpiX, dpiY, layers, activeIndex, extensions);
            return new ProjectReadResult(state, fingerprint, minor > ProjectFormat.MinorVersion, _damaged);
        }

        private (List<Layer> Layers, int Active, int Width, int Height, double DpiX, double DpiY) BuildLayers(DocumentDto doc)
        {
            var canvas = doc.Canvas ?? throw new ProjectFormatException("The project has no canvas.");
            if (!ProjectFormat.IsValidCanvas(canvas.Width, canvas.Height))
            {
                throw new ProjectFormatException("The project's canvas size isn't supported.");
            }

            int w = canvas.Width, h = canvas.Height;
            if (doc.Layers is not { Count: > 0 } layerDtos || layerDtos.Count > 10_000)
            {
                throw new ProjectFormatException("The project has no layers.");
            }

            var ids = new HashSet<Guid>();
            var layers = new List<Layer>();
            foreach (var ld in layerDtos)
            {
                var elements = new List<LayerElement>();
                foreach (var ed in ld.Elements ?? [])
                {
                    LayerElement element = ed switch
                    {
                        RasterDto r => new RasterSegment(ReadSurface(r.Pixels, w, h, mask: false) ?? new TiledSurface(w, h), ReadSurface(r.Erase, w, h, mask: true)) { Id = NewId(ids, r.Id) },
                        TextDto t => ReadText(t, NewId(ids, t.Id)),
                        _ => throw new ProjectFormatException("The project contains an unknown element."),
                    };
                    elements.Add(element);
                }

                if (elements.Count == 0 || elements[0] is not RasterSegment || elements[^1] is not RasterSegment)
                {
                    throw new ProjectFormatException("A layer's element stack is invalid.");
                }

                layers.Add(new Layer(Truncate(ld.Name, 256) ?? "Layer", elements)
                {
                    Id = NewId(ids, ld.Id),
                    Visible = ld.Visible,
                    Opacity = Finite(ld.Opacity, 1, 0, 1),
                    BlendMode = ParseBlend(ld.BlendMode),
                    IsBackground = ld.IsBackground && layers.Count == 0,
                    IsTransparent = ld.IsTransparent || !(ld.IsBackground && layers.Count == 0),
                });
            }

            var active = layers.FindIndex(l => ProjectFormat.IdString(l.Id) == doc.ActiveLayerId);
            return (layers, active < 0 ? layers.Count - 1 : active, w, h, Finite(canvas.DpiX, 96, 1, 100_000), Finite(canvas.DpiY, 96, 1, 100_000));
        }

        private TiledSurface? ReadSurface(PixelsDto? p, int w, int h, bool mask)
        {
            if (p is null)
            {
                return null;
            }

            if (p.Fill is not null)
            {
                return mask ? null : TiledSurface.CreateFilled(w, h, ColorUtil.FromColor(ProjectImages.ParseHex(p.Fill)));
            }

            if (p is not { Part: { } part, X: { } x, Y: { } y, Width: { } pw, Height: { } ph }
                || pw <= 0 || ph <= 0 || x < 0 || y < 0 || (long)x + pw > w || (long)y + ph > h)
            {
                throw new ProjectFormatException("A layer image has an invalid position or size.");
            }

            var bytes = Optional(part, int.MaxValue);
            if (bytes is null)
            {
                return null;
            }

            var buf = mask ? ProjectImages.DecodeMaskPng(bytes, pw, ph) : ProjectImages.DecodeColorPng(bytes, pw, ph);
            var surface = new TiledSurface(w, h);
            surface.WriteRect(new PixelRect(x, y, pw, ph), buf.Pixels, 0, pw);
            surface.Compact();
            return surface;
        }

        private static TextObject ReadText(TextDto t, Guid id)
        {
            if (t.Text is { Length: > ProjectFormat.MaxTextLength })
            {
                throw new ProjectFormatException("A text object is too long.");
            }

            if (t.Box is not { Length: 4 } box || box.Any(v => !double.IsFinite(v)) || box[2] < 0 || box[3] < 0)
            {
                throw new ProjectFormatException("A text object has an invalid box.");
            }

            var m = Matrix.Identity;
            if (t.Transform is not null)
            {
                if (t.Transform.Length != 6 || t.Transform.Any(v => !double.IsFinite(v)))
                {
                    throw new ProjectFormatException("A text object has an invalid transform.");
                }

                m = new Matrix(t.Transform[0], t.Transform[1], t.Transform[2], t.Transform[3], t.Transform[4], t.Transform[5]);
                if (!m.HasInverse)
                {
                    throw new ProjectFormatException("A text object has an invalid transform.");
                }
            }

            return new TextObject
            {
                Id = id,
                Text = t.Text ?? string.Empty,
                Box = new Rect(box[0], box[1], box[2], box[3]),
                Transform = m,
                FontFamily = string.IsNullOrWhiteSpace(t.FontFamily) ? "Segoe UI" : Truncate(t.FontFamily, 256)!,
                FontSizePt = Finite(t.FontSizePt, 11, 1, 999),
                Bold = t.Bold,
                Italic = t.Italic,
                Underline = t.Underline,
                Strikethrough = t.Strikethrough,
                Foreground = t.Foreground is null ? Colors.Black : ProjectImages.ParseHex(t.Foreground),
                Background = t.Background is null ? Colors.White : ProjectImages.ParseHex(t.Background),
                OpaqueBackground = t.OpaqueBackground,
                Opacity = Finite(t.Opacity, 1, 0, 1),
            };
        }

        private ProjectExtensions ReadExtensions(ManifestDto manifest, List<Layer> layers)
        {
            var list = ImmutableArray.CreateBuilder<ExtensionData>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in manifest.Extensions ?? [])
            {
                if (!ProjectFormat.IsValidExtensionId(e.Id) || !seen.Add(e.Id!))
                {
                    continue;
                }

                var prefix = $"extensions/{e.Id}/";
                var files = ImmutableSortedDictionary.CreateBuilder<string, byte[]>(StringComparer.Ordinal);
                long size = 0;
                foreach (var (path, entry) in _entries.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    size += entry.Length;
                    if (size > ProjectFormat.MaxExtensionBytes)
                    {
                        throw new ProjectFormatException($"The data of \"{e.Id}\" is too large.");
                    }

                    if (Optional(path, int.MaxValue) is { } data)
                    {
                        files[path[prefix.Length..]] = data;
                    }
                }

                list.Add(new ExtensionData
                {
                    Id = e.Id!,
                    DisplayName = Truncate(e.DisplayName, 256),
                    Version = Truncate(e.Version, 64),
                    DataVersion = Truncate(e.DataVersion, 64),
                    InfoUrl = Truncate(e.InfoUrl, 2048),
                    Policy = e.Policy == "discard" ? ExtensionPolicy.Discard : ExtensionPolicy.Keep,
                    Files = files.ToImmutable(),
                });
            }

            var layerIds = layers.Select(l => l.Id).ToHashSet();
            var textIds = layers.SelectMany(l => l.TextObjects).Select(t => t.Id).ToHashSet();
            var anchors = ImmutableArray.CreateBuilder<Anchor>();
            var anchorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in manifest.Anchors ?? [])
            {
                if (a is not { Id: { Length: > 0 and <= 128 } id, Extension: { } ext, Target: { } t } || !seen.Contains(ext) || !anchorIds.Add(id))
                {
                    continue;
                }

                var anchor = t.Kind switch
                {
                    "document" => new Anchor(id, ext, AnchorKind.Document),
                    "layer" when ParseId(t.LayerId) is { } l && layerIds.Contains(l) => new Anchor(id, ext, AnchorKind.Layer, LayerId: l),
                    "text" when ParseId(t.TextId) is { } tx && textIds.Contains(tx) => new Anchor(id, ext, AnchorKind.Text, TextId: tx),
                    "region" when ReadShape(t.Shape) is { } shape => new Anchor(id, ext, AnchorKind.Region, LayerId: ParseId(t.LayerId) is { } rl && layerIds.Contains(rl) ? rl : null, Region: shape),
                    _ => null,
                };
                if (anchor is not null)
                {
                    anchors.Add(anchor);
                }
            }

            return list.Count == 0 && anchors.Count == 0 ? ProjectExtensions.Empty : new ProjectExtensions(list.ToImmutable(), anchors.ToImmutable());
        }

        private static RegionShape? ReadShape(ShapeDto? s)
        {
            if (s?.Type == "rect" && s.Rect is { Length: 4 } r && r.All(double.IsFinite) && r[2] >= 0 && r[3] >= 0)
            {
                return RegionShape.FromRect(new Rect(r[0], r[1], r[2], r[3]));
            }

            if (s?.Type == "polygon" && s.Points is { Length: >= 3 and <= 100_000 } pts && pts.All(p => p is { Length: 2 } && p.All(double.IsFinite)))
            {
                return RegionShape.FromPolygon(pts.Select(p => new Point(p[0], p[1])));
            }

            return null;
        }

        private byte[] Required(string path, int max, bool verify) =>
            (verify ? Optional(path, max) : (_entries.TryGetValue(path, out var e) ? ReadEntry(e, max) : null))
            ?? throw new ProjectFormatException($"The project is missing \"{path}\".");

        /// <summary>Reads a part listed in the manifest; null (counted as damaged) when missing or its hash fails.</summary>
        private byte[]? Optional(string path, int max)
        {
            if (!_entries.TryGetValue(path, out var entry) || !_hashes.TryGetValue(path, out var hash))
            {
                _damaged++;
                return null;
            }

            var data = ReadEntry(entry, max);
            if (ProjectImages.Sha256(data) != hash)
            {
                _damaged++;
                return null;
            }

            return data;
        }

        private byte[] ReadEntry(ZipArchiveEntry entry, int max)
        {
            if (entry.Length > max || entry.Length > int.MaxValue - 1 || entry.Length > _budget)
            {
                throw new ProjectFormatException("The project exceeds a size limit.");
            }

            _budget -= entry.Length;
            var data = new byte[entry.Length];
            using var s = entry.Open();
            s.ReadExactly(data);
            if (s.ReadByte() != -1)
            {
                throw new ProjectFormatException("The project is damaged.");
            }

            return data;
        }

        private static T Parse<T>(byte[] json) =>
            JsonSerializer.Deserialize<T>(json, ProjectJson.Options) ?? throw new ProjectFormatException("The project is damaged.");

        private static (int Major, int Minor) ParseVersion(string? v)
        {
            var parts = v?.Split('.');
            if (parts is not { Length: 2 }
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
            {
                throw new ProjectFormatException("The project's format version is invalid.");
            }

            return (major, minor);
        }

        private static Guid NewId(HashSet<Guid> ids, string? s)
        {
            if (ParseId(s) is not { } id || !ids.Add(id))
            {
                throw new ProjectFormatException("The project contains an invalid or duplicate id.");
            }

            return id;
        }

        private static Guid? ParseId(string? s) => Guid.TryParseExact(s, "D", out var g) && g != Guid.Empty ? g : null;

        private static BlendMode ParseBlend(string? s) =>
            Enum.TryParse<BlendMode>(s, ignoreCase: true, out var b) && Enum.IsDefined(b) && !int.TryParse(s, out _) ? b : BlendMode.Normal;

        private static double Finite(double v, double fallback, double min, double max) =>
            double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

        private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
    }
}
