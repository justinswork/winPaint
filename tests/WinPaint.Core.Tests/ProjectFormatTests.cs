using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;
using WinPaint.Core.Projects;
using static WinPaint.Core.Tests.TestUtil;

namespace WinPaint.Core.Tests;

/// <summary>The winPaint project format (docs/specs/wpp-format.md): container, embedding, limits.</summary>
public sealed class ProjectFormatTests
{
    private const string ExtId = "com.contoso.hotspots";

    [Fact]
    public void Container_RoundTrips_LayersTextPixelsEraseAndExtensions() => Sta(() =>
    {
        var doc = BuildRichDocument();
        var before = doc.CaptureState();
        var bytes = ProjectWriter.WriteToBytes(before, new ProjectWriteOptions { Preview = doc.Flatten() });
        var read = ProjectReader.Read(bytes);
        var after = PaintDocument.FromState(read.State);

        Assert.Equal(0, read.DamagedParts);
        Assert.False(read.IsNewerMinorVersion);
        Assert.False(after.IsDirty);
        Assert.Equal((doc.Width, doc.Height, doc.DpiX, doc.DpiY, doc.ActiveLayerIndex), (after.Width, after.Height, after.DpiX, after.DpiY, after.ActiveLayerIndex));
        Assert.Equal(doc.Layers.Count, after.Layers.Count);
        for (var i = 0; i < doc.Layers.Count; i++)
        {
            var a = doc.Layers[i];
            var b = after.Layers[i];
            Assert.Equal((a.Id, a.Name, a.Visible, a.Opacity, a.BlendMode, a.IsBackground, a.IsTransparent), (b.Id, b.Name, b.Visible, b.Opacity, b.BlendMode, b.IsBackground, b.IsTransparent));
            Assert.Equal(a.Elements.Count, b.Elements.Count);
            for (var j = 0; j < a.Elements.Count; j++)
            {
                Assert.Equal(a.Elements[j].Id, b.Elements[j].Id);
                switch (a.Elements[j])
                {
                    case TextObject t:
                        Assert.True(t.StateEquals((TextObject)b.Elements[j]));
                        break;
                    case RasterSegment s:
                        var s2 = (RasterSegment)b.Elements[j];
                        Assert.True(s.Pixels.ContentEquals(s2.Pixels), $"pixels of layer {i} element {j}");
                        Assert.Equal(MaskBytes(s.Erase), MaskBytes(s2.Erase));
                        break;
                }
            }
        }

        Assert.True(doc.Flatten().ContentEquals(after.Flatten()));
        var ext = Assert.Single(after.Extensions.Extensions);
        Assert.Equal("Contoso Hotspots", ext.DisplayName);
        Assert.Equal("[1,2,3]", Encoding.UTF8.GetString(ext.Files["data/hotspots.json"]));
        Assert.Equal(4, after.Extensions.Anchors.Length);
        Assert.Equal(new Rect(10, 20, 30, 40), after.Extensions.Anchors.Single(a => a.Kind == AnchorKind.Region).Region!.Rect);
    });

    [Fact]
    public void Container_Layout_MimetypeFirstAndStored_PartsHashed() => Sta(() =>
    {
        var doc = BuildRichDocument();
        var bytes = ProjectWriter.WriteToBytes(doc.CaptureState(), new ProjectWriteOptions { Preview = doc.Flatten() });
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var first = zip.Entries[0];
        Assert.Equal("mimetype", first.FullName);
        Assert.Equal(first.Length, first.CompressedLength);
        Assert.Equal(ProjectFormat.MediaType, new StreamReader(first.Open()).ReadToEnd());
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("manifest.json", names);
        Assert.Contains("document.json", names);
        Assert.Contains("preview.png", names);
        Assert.Contains("thumbnail.png", names);
        Assert.Contains($"extensions/{ExtId}/data/hotspots.json", names);

        var manifest = JsonNode.Parse(zip.GetEntry("manifest.json")!.Open())!;
        Assert.Equal("1.0", (string?)manifest["formatVersion"]);
        var parts = manifest["parts"]!.AsArray().Select(p => (string)p!["path"]!).ToHashSet();
        Assert.Equal(names.Where(n => n is not "mimetype" and not "manifest.json").ToHashSet(), parts);

        // A plain white background is stored as a fill, not as a PNG.
        var blank = ProjectWriter.WriteToBytes(new PaintDocument(500, 400).CaptureState());
        using var blankZip = new ZipArchive(new MemoryStream(blank));
        var document = JsonNode.Parse(blankZip.GetEntry("document.json")!.Open())!;
        Assert.Equal("#FFFFFFFF", (string?)document["layers"]![0]!["elements"]![0]!["pixels"]!["fill"]);
        Assert.DoesNotContain(blankZip.Entries, e => e.FullName.EndsWith(".png", StringComparison.Ordinal));
    });

    [Fact]
    public void Writer_DropsDiscardData_OnlyWhenPluginMissing() => Sta(() =>
    {
        var doc = BuildRichDocument();
        doc.Extensions = doc.Extensions with { Extensions = [doc.Extensions.Extensions[0] with { Policy = ExtensionPolicy.Discard }] };
        var state = doc.CaptureState();

        var missing = ProjectReader.Read(ProjectWriter.WriteToBytes(state));
        Assert.True(missing.State.Extensions.IsEmpty);

        var installed = ProjectReader.Read(ProjectWriter.WriteToBytes(state, new ProjectWriteOptions { IsExtensionInstalled = id => id == ExtId }));
        Assert.Equal(ExtensionPolicy.Discard, Assert.Single(installed.State.Extensions.Extensions).Policy);
        Assert.Equal(4, installed.State.Extensions.Anchors.Length);
    });

    [Theory]
    [InlineData(ImageFormat.Png)]
    [InlineData(ImageFormat.Tiff)]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.Gif)]
    public void Embedded_RestoresProject_AndImageStillDecodesTheSame(ImageFormat format) => Sta(() =>
    {
        var doc = BuildRichDocument();
        var flat = doc.Flatten();
        var plain = ProjectFiles.EncodeImage(flat, format, null, null);
        var withProject = ProjectFiles.EncodeImage(flat, format, null, doc.CaptureState());

        Assert.NotNull(ProjectEmbedding.Extract(withProject));
        Assert.Null(ProjectEmbedding.Extract(plain));
        var plainPixels = ImageCodec.Decode(new MemoryStream(plain)).Pixels;
        Assert.True(plainPixels.ContentEquals(ImageCodec.Decode(new MemoryStream(withProject)).Pixels));

        var opened = ProjectFiles.OpenImage(withProject);
        Assert.Equal(EmbeddedProjectStatus.Restored, opened.Status);
        Assert.True(opened.UseProject);
        var restored = PaintDocument.FromState(opened.Project!.State);
        Assert.Equal(doc.Layers.Count, restored.Layers.Count);
        Assert.Equal("Draft v1", restored.AllText.Single().Text.Text);
    });

    [Fact]
    public void Embedded_LargePayload_SplitsAcrossJpegSegments() => Sta(() =>
    {
        var doc = new PaintDocument(64, 64);
        doc.Extensions = new ProjectExtensions([new ExtensionData { Id = ExtId, Files = ImmutableSortedDictionary.Create<string, byte[]>(StringComparer.Ordinal).Add("big.bin", RandomBytes(200_000)) }], []);
        var jpg = ProjectFiles.EncodeImage(doc.Flatten(), ImageFormat.Jpeg, null, doc.CaptureState());
        var opened = ProjectFiles.OpenImage(jpg);
        Assert.Equal(EmbeddedProjectStatus.Restored, opened.Status);
        Assert.Equal(200_000, opened.Project!.State.Extensions.Extensions[0].Files["big.bin"].Length);
    });

    [Fact]
    public void Embedded_ImageChangedOutside_IsNotRestored() => Sta(() =>
    {
        var doc = BuildRichDocument();
        var png = ProjectFiles.EncodeImage(doc.Flatten(), ImageFormat.Png, null, doc.CaptureState());
        var container = ProjectEmbedding.Extract(png)!;

        // Another program edits the picture but keeps the (unsafe-to-copy) chunk.
        var edited = doc.Flatten();
        edited.Fill(new PixelRect(0, 0, 5, 5), ColorUtil.Black);
        var tampered = ProjectEmbedding.Embed(ImageCodec.EncodePng(edited), ImageFormat.Png, container);

        var opened = ProjectFiles.OpenImage(tampered);
        Assert.Equal(EmbeddedProjectStatus.ChangedOutside, opened.Status);
        Assert.False(opened.UseProject);
        Assert.NotNull(opened.Project);
        Assert.True(edited.ContentEquals(opened.Image!.Pixels));
    });

    [Fact]
    public void Embedded_DamagedPayload_IsIgnored() => Sta(() =>
    {
        var doc = BuildRichDocument();
        var png = ProjectFiles.EncodeImage(doc.Flatten(), ImageFormat.Png, null, doc.CaptureState());
        var i = Encoding.ASCII.GetString(png).IndexOf("wpRJ", StringComparison.Ordinal);
        png[i + 100] ^= 0xFF;
        var opened = ProjectFiles.OpenImage(png);
        Assert.Equal(EmbeddedProjectStatus.Ignored, opened.Status);
        Assert.NotNull(opened.IgnoredReason);
        Assert.NotNull(opened.Image);
    });

    [Fact]
    public void Save_WithoutProject_IsByteIdenticalToPlainEncode() => Sta(() =>
    {
        var flat = RandomImage(40, 30, 3);
        using var ms = new MemoryStream();
        ImageCodec.Encode(flat, ms, ImageFormat.Png);
        Assert.Equal(ms.ToArray(), ProjectFiles.EncodeImage(flat, ImageFormat.Png, null, null));
    });

    [Fact]
    public void Reader_RejectsNewerMajor_AndFlagsNewerMinor() => Sta(() =>
    {
        var bytes = ProjectWriter.WriteToBytes(new PaintDocument(10, 10).CaptureState());
        var newer = Rewrite(bytes, (name, data) => name == "manifest.json" ? SetJson(data, m => m["formatVersion"] = "2.0") : data);
        var ex = Assert.Throws<ProjectFormatException>(() => ProjectReader.Read(newer));
        Assert.True(ex.IsNewerMajorVersion);

        var minor = Rewrite(bytes, (name, data) => name == "manifest.json" ? SetJson(data, m => m["formatVersion"] = "1.7") : data);
        Assert.True(ProjectReader.Read(minor).IsNewerMinorVersion);
    });

    [Fact]
    public void Reader_PartWithWrongHash_IsTreatedAsMissing() => Sta(() =>
    {
        var doc = BuildRichDocument();
        var bytes = ProjectWriter.WriteToBytes(doc.CaptureState());
        var layerPart = $"layers/{ProjectFormat.IdString(doc.Layers[0].Id)}/{ProjectFormat.IdString(doc.Layers[0].Elements[0].Id)}.png";
        var otherPng = ImageCodec.EncodePng(RandomImage(1, 1, 1));
        var broken = Rewrite(bytes, (name, data) => name.StartsWith("layers/", StringComparison.Ordinal) && name != layerPart ? otherPng : data);
        var read = ProjectReader.Read(broken);
        Assert.True(read.DamagedParts > 0);
    });

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("/abs.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("C:/x.txt")]
    [InlineData("extensions/com.x.y/./a")]
    public void Reader_RejectsUnsafePaths(string path) => Sta(() =>
    {
        var bytes = ProjectWriter.WriteToBytes(new PaintDocument(10, 10).CaptureState());
        var evil = Rewrite(bytes, (_, d) => d, extra: (path, [1, 2, 3]));
        Assert.Throws<ProjectFormatException>(() => ProjectReader.Read(evil));
    });

    [Fact]
    public void Reader_RejectsZipBombAndHugeCanvas() => Sta(() =>
    {
        var rich = ProjectWriter.WriteToBytes(BuildRichDocument().CaptureState());
        var bomb = Rewrite(rich, (name, d) => name.StartsWith("extensions/", StringComparison.Ordinal) ? new byte[50_000_000] : d, rehash: true);
        Assert.Throws<ProjectFormatException>(() => ProjectReader.Read(bomb));

        var bytes = ProjectWriter.WriteToBytes(new PaintDocument(10, 10).CaptureState());

        var huge = Rewrite(bytes, (name, data) => name == "document.json" ? SetJson(data, d => d["canvas"]!["width"] = 200_000) : data, rehash: true);
        Assert.Throws<ProjectFormatException>(() => ProjectReader.Read(huge));
    });

    [Fact]
    public void Reader_RejectsGarbage()
    {
        Assert.Throws<ProjectFormatException>(() => ProjectReader.Read(Encoding.ASCII.GetBytes("not a zip at all")));
        Assert.Throws<ProjectFormatException>(() => ProjectEmbedding.Extract([.. ProjectEmbeddingPngWith("XXXX")]));
    }

    [Fact]
    public void WorthKeeping_DetectsTextLayersAndExtensions() => Sta(() =>
    {
        var doc = new PaintDocument(50, 50);
        Assert.False(ProjectFormat.IsWorthKeeping(doc.CaptureState()));
        TextOperations.Insert(doc, doc.ActiveLayer, new TextObject { Text = "x", Box = new Rect(0, 0, 20, 20) });
        Assert.True(ProjectFormat.IsWorthKeeping(doc.CaptureState()));
        TextOperations.FlattenAll(doc, doc.ActiveLayer);
        Assert.False(ProjectFormat.IsWorthKeeping(doc.CaptureState()));
        doc.Layers[0].Opacity = 0.5;
        Assert.True(ProjectFormat.IsWorthKeeping(doc.CaptureState()));
    });

    [Fact]
    public void RegionShape_StaysRectUnderQuarterTurns_BecomesPolygonOtherwise()
    {
        var r = RegionShape.FromRect(new Rect(10, 20, 30, 40));
        var m = Matrix.Identity;
        m.Rotate(90);
        Assert.NotNull(r.Transform(m).Rect);
        var m2 = Matrix.Identity;
        m2.Rotate(30);
        Assert.Null(r.Transform(m2).Rect);
        Assert.Equal(4, r.Transform(m2).Points.Length);
    }

    [Fact]
    public void ExtensionIds_AndPaths_AreValidated()
    {
        Assert.True(ProjectFormat.IsValidExtensionId("com.contoso.hotspots"));
        Assert.True(ProjectFormat.IsValidExtensionId("app.winpaint.labs-tags"));
        Assert.False(ProjectFormat.IsValidExtensionId("Com.Contoso"));
        Assert.False(ProjectFormat.IsValidExtensionId("ab"));
        Assert.False(ProjectFormat.IsValidExtensionId("a/b.c"));
        Assert.True(ProjectFormat.IsValidPartPath("layers/a/b.png"));
        Assert.False(ProjectFormat.IsValidPartPath("layers//b.png"));
    }

    /// <summary>White canvas, painted pixels with partial alpha, live text, erase mask, a hidden blended layer, extension data.</summary>
    internal static PaintDocument BuildRichDocument()
    {
        var doc = new PaintDocument(300, 200) { DpiX = 120, DpiY = 120 };
        var bg = doc.Layers[0];
        var paint = RandomImage(60, 40, 7, opaque: false);
        bg.TopSegment.Pixels.WriteRect(new PixelRect(20, 30, 60, 40), paint.Pixels, 0, 60);
        var text = new TextObject
        {
            Text = "Draft v1",
            Box = new Rect(100, 120, 140, 40),
            FontFamily = "Segoe UI",
            FontSizePt = 20,
            Bold = true,
            Foreground = Color.FromArgb(200, 10, 20, 30),
            Background = Colors.Yellow,
            OpaqueBackground = true,
            Opacity = 0.8,
        };
        var m = Matrix.Identity;
        m.RotateAt(15, 150, 140);
        text.Transform = m;
        TextOperations.Insert(doc, bg, text);
        bg.TopSegment.Pixels.FillRect(new PixelRect(110, 125, 30, 10), ColorUtil.Premultiply(128, 255, 0, 0));
        var erase = bg.TopSegment.EnsureErase();
        erase.FillRect(new PixelRect(200, 10, 20, 20), 255);
        erase.FillRect(new PixelRect(205, 15, 5, 5), 77);

        var top = LayerOperations.Add(doc, "Overlay");
        top.Visible = false;
        top.Opacity = 0.6;
        top.BlendMode = BlendMode.Multiply;
        top.TopSegment.Pixels.FillRect(new PixelRect(0, 0, 50, 50), TestUtil.Rgb(0, 128, 255));
        doc.ActiveLayerIndex = 0;

        var files = ImmutableSortedDictionary.Create<string, byte[]>(StringComparer.Ordinal).Add("data/hotspots.json", Encoding.UTF8.GetBytes("[1,2,3]"));
        doc.Extensions = new ProjectExtensions(
            [new ExtensionData { Id = ExtId, DisplayName = "Contoso Hotspots", Version = "2.3.0", DataVersion = "1", InfoUrl = "https://contoso.example", Files = files }],
            [
                new Anchor("a1", ExtId, AnchorKind.Document),
                new Anchor("a2", ExtId, AnchorKind.Layer, LayerId: top.Id),
                new Anchor("a3", ExtId, AnchorKind.Text, TextId: text.Id),
                new Anchor("a4", ExtId, AnchorKind.Region, Region: RegionShape.FromRect(new Rect(10, 20, 30, 40))),
            ]);
        doc.Commit("Build");
        return doc;
    }

    private static byte[]? MaskBytes(TiledSurface? erase) =>
        erase?.ToPixelBuffer().Pixels.Select(p => (byte)p).ToArray() is { } b && b.Any(v => v != 0) ? b : null;

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        new Random(5).NextBytes(b);
        return b;
    }

    private static byte[] SetJson(byte[] json, Action<JsonNode> change)
    {
        var node = JsonNode.Parse(json)!;
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    /// <summary>Copies a container, transforming entries; optionally adds an entry and fixes up manifest hashes.</summary>
    private static byte[] Rewrite(byte[] container, Func<string, byte[], byte[]> change, (string Name, byte[] Data)? extra = null, bool rehash = false)
    {
        var entries = new List<(string Name, byte[] Data)>();
        using (var zip = new ZipArchive(new MemoryStream(container)))
        {
            foreach (var e in zip.Entries)
            {
                using var ms = new MemoryStream();
                e.Open().CopyTo(ms);
                entries.Add((e.FullName, change(e.FullName, ms.ToArray())));
            }
        }

        if (extra is { } x)
        {
            entries.Add(x);
        }

        if (rehash)
        {
            var hashes = entries.ToDictionary(e => e.Name, e => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(e.Data)));
            var i = entries.FindIndex(e => e.Name == "manifest.json");
            entries[i] = ("manifest.json", SetJson(entries[i].Data, m =>
            {
                foreach (var p in m["parts"]!.AsArray())
                {
                    p!["sha256"] = hashes[(string)p["path"]!];
                }
            }));
        }

        using var outMs = new MemoryStream();
        using (var zip = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name, name == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(data);
            }
        }

        return outMs.ToArray();
    }

    /// <summary>A tiny PNG carrying a wpRJ chunk whose payload has the given magic.</summary>
    private static byte[] ProjectEmbeddingPngWith(string magic)
    {
        var png = ImageCodec.EncodePng(RandomImage(2, 2, 1));
        var embedded = ProjectEmbedding.Embed(png, ImageFormat.Png, [9, 9, 9]);
        var i = Encoding.ASCII.GetString(embedded).IndexOf("WPP1", StringComparison.Ordinal);
        Encoding.ASCII.GetBytes(magic).CopyTo(embedded, i);

        // Fix the chunk CRC so only the magic is wrong.
        var start = i - 8;
        var len = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(embedded.AsSpan(start));
        var crc = Crc(embedded.AsSpan(start + 4, 4 + len));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(embedded.AsSpan(start + 8 + len), crc);
        return embedded;
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
