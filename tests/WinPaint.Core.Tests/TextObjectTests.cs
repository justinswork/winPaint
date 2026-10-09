using System.Windows;
using System.Windows.Media;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.Text;
using WinPaint.Core.Tools;
using static WinPaint.Core.Tests.TestUtil;

namespace WinPaint.Core.Tests;

/// <summary>Section 6.9 text acceptance tests that run headless against the Core.</summary>
public class TextObjectTests
{
    private static TextObject Template(double sizePt = 24) => new()
    {
        FontFamily = "Segoe UI",
        FontSizePt = sizePt,
        Foreground = Colors.Black,
        Background = Colors.White,
    };

    private static TextObject CreateText(PaintDocument doc, string text, Rect box, TextObject? template = null)
    {
        var s = TextEditSession.BeginNew(doc, box, template ?? Template());
        s.Update(t => t.Text = text);
        Assert.Equal(TextEditOutcome.Created, s.Commit());
        return s.Text;
    }

    private static TextEditSession Reopen(PaintDocument doc, Point p)
    {
        var hit = doc.HitTestText(p);
        Assert.NotNull(hit);
        return TextEditSession.BeginExisting(doc, hit);
    }

    private static int CountNonWhite(PixelBuffer b, PixelRect r)
    {
        var n = 0;
        for (var y = r.Y; y < r.Bottom; y++)
        {
            for (var x = r.X; x < r.Right; x++)
            {
                if (b[x, y] != ColorUtil.White)
                {
                    n++;
                }
            }
        }

        return n;
    }

    private static void PaintRedStroke(PaintDocument doc, FakeHost host, params Point[] pts)
    {
        host.Settings.Primary = Colors.Red;
        host.Settings.BrushSize = 6;
        FakeHost.Drag(new BrushTool(host), PointerButton.Left, pts);
    }

    [Fact]
    public void T01_CreateCommit() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var t = CreateText(doc, "Hello", new Rect(20, 20, 200, 40));
        Assert.Single(doc.AllText);
        Assert.Equal(1, doc.History.UndoCount);
        Assert.True(CountNonWhite(doc.Flatten(), new PixelRect(20, 20, 200, 50)) > 50);
        Assert.Equal("Hello", t.Text);
    });

    [Fact]
    public void T02_ReopenShowsExactState() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var tpl = Template(30);
        tpl.Foreground = Colors.DarkGreen;
        tpl.Bold = true;
        CreateText(doc, "Hello", new Rect(20, 20, 200, 40), tpl);
        var s = Reopen(doc, new Point(40, 35));
        Assert.Equal("Hello", s.Text.Text);
        Assert.Equal(30, s.Text.FontSizePt);
        Assert.True(s.Text.Bold);
        Assert.Equal(Colors.DarkGreen, s.Text.Foreground);
        Assert.Equal(TextEditOutcome.NoChange, s.Commit());
        Assert.Equal(1, doc.History.UndoCount);
    });

    [Fact]
    public void T05_KeepsItsPlaceAbove() => Sta(() =>
    {
        var doc = new PaintDocument(500, 200);
        var host = new FakeHost(doc);
        CreateText(doc, "Hello", new Rect(20, 20, 300, 60));
        PaintRedStroke(doc, host, new Point(10, 40), new Point(200, 45), new Point(400, 50));
        var strokeSegment = doc.ActiveLayer.TopSegment.Pixels.ToPixelBuffer();
        var before = doc.Flatten();

        var s = Reopen(doc, new Point(30, 30));
        s.Update(t => t.Text = "Goodbye world");
        Assert.Equal(TextEditOutcome.Edited, s.Commit());

        var after = doc.Flatten();
        Assert.True(strokeSegment.ContentEquals(doc.ActiveLayer.TopSegment.Pixels.ToPixelBuffer()));
        var red = 0;
        for (var i = 0; i < strokeSegment.Pixels.Length; i++)
        {
            if (ColorUtil.A(strokeSegment.Pixels[i]) == 255)
            {
                red++;
                Assert.Equal(before.Pixels[i], after.Pixels[i]);
                Assert.Equal(strokeSegment.Pixels[i], after.Pixels[i]);
            }
        }

        Assert.True(red > 500);
        Assert.False(before.ContentEquals(after));
    });

    [Fact]
    public void T06_KeepsItsPlaceBelow() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var blue = Rgb(0, 0, 255);
        doc.ActiveLayer.TopSegment.Pixels.FillRect(new PixelRect(10, 10, 300, 100), blue);
        doc.Invalidate(doc.ActiveLayer, doc.Bounds);
        doc.Commit("rect");
        var tpl = Template();
        tpl.OpaqueBackground = true;
        CreateText(doc, "Hi", new Rect(50, 30, 200, 50), tpl);
        Assert.Equal(ColorUtil.White, doc.CompositePixel(240, 40));

        var s = Reopen(doc, new Point(60, 40));
        s.Update(t => t.OpaqueBackground = false);
        s.Commit();
        Assert.Equal(blue, doc.CompositePixel(240, 40));
    });

    [Fact]
    public void T07_TransparentErasePersists() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200, 0);
        Assert.True(doc.ActiveLayer.ErasesToTransparent);
        var tpl = Template(40);
        tpl.OpaqueBackground = true;
        tpl.Background = Colors.Yellow;
        CreateText(doc, "WWWW", new Rect(20, 20, 300, 80), tpl);
        var host = new FakeHost(doc);
        host.Settings.EraserSize = 20;
        FakeHost.Drag(new EraserTool(host), PointerButton.Left, new Point(10, 50), new Point(390, 50));
        Assert.Equal(0u, doc.CompositePixel(100, 50));

        var s = Reopen(doc, new Point(30, 30));
        s.Update(t =>
        {
            t.Text = "MMMMMM";
            t.Background = Colors.Orange;
        });
        s.Commit();
        for (var x = 20; x < 320; x += 7)
        {
            Assert.Equal(0u, doc.CompositePixel(x, 50));
        }

        Assert.NotEqual(0u, doc.CompositePixel(100, 25));
    });

    [Fact]
    public void T08_MoveResizeRewraps() => Sta(() =>
    {
        var doc = new PaintDocument(600, 400);
        doc.ActiveLayer.TopSegment.Pixels.FillRect(new PixelRect(0, 0, 600, 400), Rgb(200, 220, 240));
        doc.Commit("bg");
        var pristine = doc.Flatten();
        var t = CreateText(doc, "one two three four five six", new Rect(20, 20, 500, 30));
        var oneLine = TextLayoutEngine.EffectiveBox(t).Height;

        var s = Reopen(doc, new Point(30, 30));
        s.Update(x => x.Box = new Rect(x.Box.X, x.Box.Y, 90, x.Box.Height));
        Assert.True(TextLayoutEngine.EffectiveBox(t).Height > oneLine * 2.5);
        s.Update(x => x.Box = new Rect(300, 200, 90, x.Box.Height));
        s.Commit();

        var now = doc.Flatten();
        for (var y = 20; y < 50; y++)
        {
            for (var x = 20; x < 290; x++)
            {
                Assert.Equal(pristine[x, y], now[x, y]);
            }
        }

        Assert.NotNull(doc.HitTestText(new Point(310, 210)));
        Assert.Null(doc.HitTestText(new Point(30, 30)));
    });

    [Fact]
    public void T09_FormattingOneStepEach() => Sta(() =>
    {
        var doc = new PaintDocument(500, 300);
        CreateText(doc, "Format me", new Rect(20, 20, 400, 60));
        var stepsBefore = doc.History.UndoCount;
        var original = doc.Flatten();
        var s = Reopen(doc, new Point(30, 30));
        var hashes = new HashSet<string> { original.ContentHash() };
        Action<TextObject>[] changes =
        [
            t => t.FontFamily = "Times New Roman",
            t => t.FontSizePt = 36,
            t => t.Bold = true,
            t => t.Italic = true,
            t => t.Underline = true,
            t => t.Strikethrough = true,
            t => t.Foreground = Colors.Red,
            t => t.OpaqueBackground = true,
            t => t.Background = Colors.LightBlue,
        ];
        foreach (var c in changes)
        {
            s.Update(c);
            Assert.True(hashes.Add(doc.Flatten().ContentHash()), "each formatting change must render differently");
        }

        s.Commit();
        Assert.Equal(stepsBefore + 1, doc.History.UndoCount);
        doc.Undo();
        Assert.True(original.ContentEquals(doc.Flatten()));
        var restored = doc.AllText.Single().Text;
        Assert.Equal("Segoe UI", restored.FontFamily);
        Assert.False(restored.Bold);
    });

    [Fact]
    public void T10_DeleteRestoresPixelsUnderneath() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var host = new FakeHost(doc);
        PaintRedStroke(doc, host, new Point(10, 10), new Point(300, 150));
        var beforeText = doc.Flatten();
        var tpl = Template();
        tpl.OpaqueBackground = true;
        CreateText(doc, "Delete me", new Rect(20, 20, 250, 60), tpl);
        Assert.False(beforeText.ContentEquals(doc.Flatten()));

        var s = Reopen(doc, new Point(30, 30));
        Assert.Equal(TextEditOutcome.Deleted, s.Delete());
        Assert.True(beforeText.ContentEquals(doc.Flatten()));
        Assert.Empty(doc.AllText);

        doc.Undo();
        Assert.Single(doc.AllText);
        Assert.Equal("Delete me", doc.AllText.Single().Text.Text);
        Assert.NotNull(doc.HitTestText(new Point(30, 30)));
    });

    [Fact]
    public void T11_UndoRedoChain() => Sta(() =>
    {
        var doc = new PaintDocument(500, 300);
        var states = new List<(string Hash, string? Text)>();
        void Snap() => states.Add((doc.Flatten().ContentHash(), doc.AllText.SingleOrDefault()?.Text.RenderKey()));

        Snap();
        CreateText(doc, "Chain", new Rect(20, 20, 300, 50));
        Snap();
        var s = Reopen(doc, new Point(30, 30));
        s.Update(t => t.Text = "Chain edited");
        s.Commit();
        Snap();
        s = Reopen(doc, new Point(30, 30));
        s.Update(t => t.Box = new Rect(100, 120, t.Box.Width, t.Box.Height));
        s.Commit();
        Snap();
        s = Reopen(doc, new Point(110, 130));
        s.Update(t => t.Bold = true);
        s.Commit();
        Snap();
        s = Reopen(doc, new Point(110, 130));
        s.Delete();
        Snap();

        for (var i = 5; i >= 1; i--)
        {
            Assert.True(doc.Undo());
            Assert.Equal(states[i - 1].Hash, doc.Flatten().ContentHash());
            Assert.Equal(states[i - 1].Text, doc.AllText.SingleOrDefault()?.Text.RenderKey());
        }

        for (var i = 1; i <= 5; i++)
        {
            Assert.True(doc.Redo());
            Assert.Equal(states[i].Hash, doc.Flatten().ContentHash());
            Assert.Equal(states[i].Text, doc.AllText.SingleOrDefault()?.Text.RenderKey());
        }
    });

    [Fact]
    public void T20_HitPriorityTopmost() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var a = CreateText(doc, "Bottom", new Rect(20, 20, 200, 60));
        var b = CreateText(doc, "Top", new Rect(60, 30, 200, 60));
        Assert.Equal(b.Id, doc.HitTestText(new Point(100, 50))!.Text.Id);
        Assert.Equal(a.Id, doc.HitTestText(new Point(30, 25))!.Text.Id);
    });

    [Fact]
    public void T21_EmptyDiscard() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        var before = doc.Flatten();
        var s = TextEditSession.BeginNew(doc, new Rect(20, 20, 100, 30), Template());
        Assert.Equal(TextEditOutcome.Discarded, s.Commit());
        Assert.Empty(doc.AllText);
        Assert.Equal(0, doc.History.UndoCount);
        Assert.Single(doc.ActiveLayer.Elements);
        Assert.True(before.ContentEquals(doc.Flatten()));

        s = TextEditSession.BeginNew(doc, new Rect(20, 20, 100, 30), Template());
        s.Update(t => t.Text = "   \n ");
        Assert.Equal(TextEditOutcome.Discarded, s.Commit());
        Assert.Equal(0, doc.History.UndoCount);
    });

    [Fact]
    public void ExistingCommittedEmptyIsDelete() => Sta(() =>
    {
        var doc = new PaintDocument(400, 200);
        CreateText(doc, "x", new Rect(20, 20, 100, 30));
        var s = Reopen(doc, new Point(25, 25));
        s.Update(t => t.Text = string.Empty);
        Assert.Equal(TextEditOutcome.Deleted, s.Commit());
        Assert.Equal(2, doc.History.UndoCount);
        Assert.Empty(doc.AllText);
    });
}
