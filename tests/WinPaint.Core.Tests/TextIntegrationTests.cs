using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;
using WinPaint.Core.Text;
using WinPaint.Core.Tools;
using static WinPaint.Core.Tests.TestUtil;

namespace WinPaint.Core.Tests;

/// <summary>Section 6.9 text tests involving tools, transforms, selection, layers and files.</summary>
public class TextIntegrationTests
{
    private static TextObject Add(FakeHost host, string text, Rect box, double size = 24)
    {
        host.Settings.FontSizePt = size;
        host.BeginNewText(box);
        host.Text.Session!.Update(t => t.Text = text);
        host.CommitTextEdit();
        return host.Document.AllText.Last().Text;
    }

    private static int CountInk(PixelBuffer b, PixelRect r, uint background = ColorUtil.White)
    {
        var n = 0;
        r = r.Intersect(b.Bounds);
        for (var y = r.Y; y < r.Bottom; y++)
        {
            for (var x = r.X; x < r.Right; x++)
            {
                if (b[x, y] != background)
                {
                    n++;
                }
            }
        }

        return n;
    }

    [Fact]
    public void T03_ReopenWithTextToolCreatesNothingExtra() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        Add(host, "Hello", new Rect(20, 20, 200, 40));
        var undo = doc.History.UndoCount;
        var tool = new TextTool(host);
        FakeHost.DoubleClick(tool, new Point(40, 35));
        Assert.True(host.IsEditingText);
        Assert.False(host.Text.Session!.IsNew);
        Assert.Equal("Hello", host.Text.Session.Text.Text);
        Assert.Single(doc.AllText);
        host.CommitTextEdit();
        Assert.Single(doc.AllText);
        Assert.Equal(undo, doc.History.UndoCount);
    });

    [Fact]
    public void T04_OtherToolsDoNotOpenEditor() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        host.Settings.FontSizePt = 30;
        host.Settings.TextOpaque = true;
        host.Settings.Secondary = Colors.Yellow;
        Add(host, "Hello", new Rect(20, 20, 200, 60));
        host.Settings.Primary = Colors.Red;
        var before = doc.History.UndoCount;
        FakeHost.DoubleClick(new PencilTool(host), new Point(150, 50));
        Assert.False(host.IsEditingText);
        Assert.Equal(ColorUtil.FromColor(Colors.Red), doc.CompositePixel(150, 50));
        Assert.Equal(before + 2, doc.History.UndoCount);
        foreach (ITool t in new ITool[] { new BrushTool(host), new FillTool(host), new EraserTool(host), new MagnifierTool(host) })
        {
            FakeHost.DoubleClick(t, new Point(40, 40));
            Assert.False(host.IsEditingText);
        }
    });

    [Fact]
    public void T02_ReopenWithSelectTool() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        Add(host, "Hello", new Rect(20, 20, 200, 40));
        var undo = doc.History.UndoCount;
        var sel = new SelectionTool(host, freeForm: false);
        FakeHost.DoubleClick(sel, new Point(40, 35));
        Assert.True(host.IsEditingText);
        Assert.Equal("Hello", host.Text.Session!.Text.Text);
        Assert.False(sel.HasSelection);
        host.CommitTextEdit();
        Assert.Equal(undo, doc.History.UndoCount);
    });

    [Fact]
    public void T12_Rotate90KeepsEditable() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        Add(host, "Rotate", new Rect(20, 20, 200, 40));
        ImageOperations.Orthogonal(doc, OrthoTransform.RotateRight);
        Assert.Equal((200, 400), (doc.Width, doc.Height));

        // Original (x, y) maps to (H − y, x): the text now lies in x∈[140,180], y∈[20,220].
        var flat = doc.Flatten();
        var inside = CountInk(flat, new PixelRect(130, 15, 60, 215));
        Assert.True(inside > 100);
        Assert.Equal(inside, CountInk(flat, flat.Bounds));

        Assert.True(host.TryBeginTextEditAt(new Point(165, 40)));
        host.Text.Session!.Update(t => t.Text = "Rotated!");
        host.CommitTextEdit();
        var after = doc.Flatten();
        Assert.Equal(CountInk(after, new PixelRect(130, 15, 60, 300)), CountInk(after, after.Bounds));
        Assert.Equal("Rotated!", doc.AllText.Single().Text.Text);
    });

    [Fact]
    public void T13_FlipResizeSkewKeepEditable() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        Add(host, "Flip me", new Rect(20, 20, 200, 40));
        ImageOperations.Orthogonal(doc, OrthoTransform.FlipHorizontal);
        var flipped = doc.Flatten();
        Assert.Equal(0, CountInk(flipped, new PixelRect(0, 0, 170, 200)));
        Assert.True(CountInk(flipped, new PixelRect(180, 15, 205, 50)) > 100);
        Assert.True(host.TryBeginTextEditAt(new Point(350, 35)));
        host.CommitTextEdit();
        ImageOperations.Orthogonal(doc, OrthoTransform.FlipHorizontal);

        // 200 % resize: the text re-renders as vectors (crisp), it is not the resampled bitmap.
        var before = doc.Flatten();
        var resampled = Resampler.Resize(before, 800, 400, ResampleMode.HighQuality);
        ImageOperations.Resize(doc, 800, 400);
        var big = doc.Flatten();
        Assert.Equal(2, doc.AllText.Single().Text.Transform.M11, 6);
        Assert.False(big.ContentEquals(resampled));
        static int Blurry(PixelBuffer b) => b.Pixels.Count(p => ColorUtil.R(p) is > 40 and < 215);
        static int Dark(PixelBuffer b) => b.Pixels.Count(p => ColorUtil.R(p) <= 40);
        Assert.True((double)Blurry(big) / Dark(big) < (double)Blurry(resampled) / Dark(resampled), "vector re-render should be sharper");
        Assert.True(host.TryBeginTextEditAt(new Point(80, 70)));
        host.Text.Session!.Update(t => t.Text = "Bigger");
        host.CommitTextEdit();

        // 20° horizontal skew.
        ImageOperations.Skew(doc, 20, 0, Colors.White);
        var t = doc.AllText.Single().Text;
        Assert.NotEqual(0, t.Transform.M21, 3);
        var center = t.Transform.Transform(new Point(t.Box.X + 20, t.Box.Y + 20));
        Assert.True(host.TryBeginTextEditAt(center));
        host.Text.Session!.Update(x => x.Text = "Skewed");
        host.CommitTextEdit();
        Assert.Equal("Skewed", doc.AllText.Single().Text.Text);
    });

    [Fact]
    public void T14_Crop() => Sta(() =>
    {
        var doc = new PaintDocument(400, 300);
        var host = new FakeHost(doc);
        Add(host, "Keep", new Rect(150, 150, 120, 40));
        Add(host, "Gone", new Rect(10, 10, 80, 30));
        ImageOperations.Crop(doc, new PixelRect(100, 100, 250, 150));
        Assert.Single(doc.AllText);
        Assert.Equal("Keep", doc.AllText.Single().Text.Text);
        Assert.True(host.TryBeginTextEditAt(new Point(60, 60)));
        host.CommitTextEdit();
        doc.Undo();
        Assert.Equal(2, doc.AllText.Count());
        Assert.NotNull(doc.HitTestText(new Point(20, 20)));
    });

    [Fact]
    public void T15_SelectionFlattensText() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        Add(host, "Flatten", new Rect(20, 20, 200, 40));
        var sel = new SelectionTool(host, false);
        FakeHost.Drag(sel, PointerButton.Left, new Point(10, 10), new Point(100, 60));
        FakeHost.Drag(sel, PointerButton.Left, new Point(50, 30), new Point(150, 130));
        sel.CommitPending();
        Assert.Empty(doc.AllText);
        Assert.Equal(1, host.FlattenNotices);
        Assert.Null(doc.HitTestText(new Point(150, 30)));
        doc.Undo();
        Assert.Single(doc.AllText);
        Assert.NotNull(doc.HitTestText(new Point(30, 30)));
    });

    [Fact]
    public void T16_CopyIncludesTextPasteIsPlain() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        Add(host, "Copy", new Rect(20, 20, 200, 50));
        var sel = new SelectionTool(host, false);
        FakeHost.Drag(sel, PointerButton.Left, new Point(10, 10), new Point(250, 80));
        var px = sel.CopySelection()!;
        Assert.True(CountInk(px, px.Bounds) > 100);
        Assert.Single(doc.AllText);
        sel.Deselect();

        var other = new PaintDocument(300, 150);
        var h2 = new FakeHost(other);
        var sel2 = new SelectionTool(h2, false);
        sel2.PasteFloating(ClipboardData.Read(ClipboardData.Create(px))!, 0, 0);
        sel2.CommitPending();
        Assert.Empty(other.AllText);
        Assert.True(CountInk(other.Flatten(), other.Bounds) > 100);
    });

    [Fact]
    public void T17_SaveKeepsSessionEditability_T18_ReopenIsPlain() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        Add(host, "Saved", new Rect(20, 20, 200, 40));
        var path = TempFile("t17.png");
        var flat = doc.Flatten();
        ImageCodec.Encode(flat, path, ImageFormat.Png);
        doc.History.MarkSaved();
        Assert.True(flat.ContentEquals(ImageCodec.Decode(path).Pixels));
        Assert.True(host.TryBeginTextEditAt(new Point(30, 30)));
        host.CommitTextEdit();
        Assert.Single(doc.AllText);

        var reopened = PaintDocument.FromImage(ImageCodec.Decode(path).Pixels);
        Assert.Empty(reopened.AllText);
        var h2 = new FakeHost(reopened);
        FakeHost.DoubleClick(new TextTool(h2), new Point(30, 30));
        Assert.False(h2.Text.Session is { IsNew: false });
        h2.CommitTextEdit();
        Assert.Empty(reopened.AllText);
        Assert.Equal(0, reopened.History.UndoCount);
        FakeHost.DoubleClick(new SelectionTool(h2, false), new Point(30, 30));
        Assert.False(h2.IsEditingText);
    });

    [Fact]
    public void T19_Layers() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        LayerOperations.Add(doc, "Layer 2");
        Add(host, "Layered", new Rect(20, 20, 200, 40));
        Assert.Single(doc.Layers[1].TextObjects);

        doc.Layers[1].Visible = false;
        doc.InvalidateAll();
        doc.Commit("hide");
        Assert.Null(doc.HitTestText(new Point(30, 30)));
        doc.Layers[1].Visible = true;
        doc.InvalidateAll();
        doc.Commit("show");

        // Duplicate gives independent objects.
        LayerOperations.Duplicate(doc, 1, "Copy");
        var a = doc.Layers[1].TextObjects.Single();
        var b = doc.Layers[2].TextObjects.Single();
        Assert.NotEqual(a.Id, b.Id);
        var s = TextEditSession.BeginExisting(doc, new TextHit(doc.Layers[2], b));
        s.Update(t => t.Text = "Changed");
        s.Commit();
        Assert.Equal("Layered", doc.Layers[1].TextObjects.Single().Text);
        LayerOperations.Delete(doc, 2);

        // Merge down keeps it editable.
        doc.ActiveLayerIndex = 1;
        LayerOperations.MergeDown(doc, 1);
        Assert.Single(doc.Layers);
        Assert.True(host.TryBeginTextEditAt(new Point(30, 30)));
        host.CommitTextEdit();

        // Delete layer + undo brings the text back.
        LayerOperations.Add(doc, "L");
        Add(host, "Doomed", new Rect(200, 100, 150, 40));
        LayerOperations.Delete(doc, 1);
        Assert.Single(doc.AllText);
        doc.Undo();
        Assert.Equal(2, doc.AllText.Count());
    });

    [Fact]
    public void T22_InvertColors() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        host.Settings.Primary = Colors.Red;
        Add(host, "Invert", new Rect(20, 20, 200, 40));
        ImageOperations.InvertColors(doc);
        var t = doc.AllText.Single().Text;
        Assert.Equal(Color.FromRgb(0, 255, 255), t.Foreground);
        Assert.Equal(ColorUtil.Black, doc.CompositePixel(300, 150));
        Assert.True(host.TryBeginTextEditAt(new Point(30, 30)));
        host.Text.Session!.Update(x => x.Text = "Still editable");
        host.CommitTextEdit();
    });

    [Fact]
    public void MoveToOtherLayerTextActivatesLayer() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        LayerOperations.Add(doc, "Layer 2");
        Add(host, "On two", new Rect(20, 20, 200, 40));
        doc.ActiveLayerIndex = 0;
        Assert.True(host.TryBeginTextEditAt(new Point(30, 30)));
        Assert.Equal(1, doc.ActiveLayerIndex);
        host.CommitTextEdit();
    });

    /// <summary>T-PERF-TEXT: editing the bottom-most of 30 texts under 30 strokes on 4K, &lt;16 ms/keystroke (×3 for CI).</summary>
    [Fact]
    public void TPerfText() => Sta(() =>
    {
        var doc = new PaintDocument(3840, 2160);
        var host = new FakeHost(doc);
        var rng = new Random(5);
        for (var i = 0; i < 30; i++)
        {
            Add(host, $"Text object {i}", new Rect(100 + (i * 110), 100 + (i * 60), 400, 60), 20);
            FakeHost.Drag(new BrushTool(host), PointerButton.Left, new Point(rng.Next(3800), rng.Next(2100)), new Point(rng.Next(3800), rng.Next(2100)));
        }

        var dirty = PixelRect.Empty;
        doc.Invalidated += (_, r) => dirty = dirty.Union(r);
        doc.Flatten();
        Assert.True(host.TryBeginTextEditAt(new Point(110, 110)));
        var session = host.Text.Session!;
        var text = session.Text.Text;
        var buffer = new uint[3840 * 2160];
        var sw = new Stopwatch();
        const int keystrokes = 40;
        for (var k = 0; k < keystrokes; k++)
        {
            dirty = PixelRect.Empty;
            sw.Start();
            text += (char)('a' + (k % 26));
            session.Update(t => t.Text = text);
            doc.RenderComposite(dirty, buffer, 0, dirty.Width);
            sw.Stop();
        }

        host.CommitTextEdit();
        var avg = sw.Elapsed.TotalMilliseconds / keystrokes;
        RecordPerf("T-PERF-TEXT", $"3840x2160, 30 text objects + 30 strokes, editing the bottom-most text: {avg:F2} ms per keystroke (average of {keystrokes})");
        Assert.True(avg < 16 * 3, $"average keystroke {avg:F1} ms");
    });
}
