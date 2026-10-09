using System.Windows;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.View;
using static WinPaint.Core.Tests.TestUtil;

namespace WinPaint.Core.Tests;

public class ViewAndHistoryTests
{
    /// <summary>A-12: screen↔canvas round trip is exact at every zoom level.</summary>
    [Fact]
    public void A12_ViewTransformRoundTrip()
    {
        double[] zooms = [0.01, 0.125, 0.25, 0.333, 0.5, 1, 1.5, 2, 3, 4, 5, 6, 7, 8];
        double[] dpis = [1, 1.25, 1.5, 2];
        foreach (var z in zooms)
        {
            foreach (var d in dpis)
            {
                var vt = new ViewTransform { Zoom = z, DpiScale = d, ScrollX = 123.5, ScrollY = 77.25, Origin = new Vector(20, 20) };
                for (var x = 0; x < 3000; x += 97)
                {
                    var p = new Point(x, x / 2);
                    var v = vt.CanvasToView(p);
                    var back = vt.ViewToCanvas(v);
                    Assert.True(Math.Abs(back.X - p.X) < 1e-9 && Math.Abs(back.Y - p.Y) < 1e-9);

                    // Center of the pixel maps back to the same pixel.
                    var center = vt.CanvasToView(new Point(x + 0.5, (x / 2) + 0.5));
                    Assert.Equal((x, x / 2), vt.ViewToPixel(center));

                    // Exactly at the pixel's top-left corner maps to that pixel too.
                    Assert.Equal((x, x / 2), vt.ViewToPixel(v));
                }
            }
        }
    }

    [Fact]
    public void ZoomAt_KeepsAnchorFixed()
    {
        var vt = new ViewTransform { Zoom = 1, ScrollX = 50, ScrollY = 40 };
        var anchor = new Point(300, 200);
        var before = vt.ViewToCanvas(anchor);
        vt.ZoomAt(4, anchor);
        var after = vt.ViewToCanvas(anchor);
        Assert.True(Math.Abs(before.X - after.X) < 1e-9 && Math.Abs(before.Y - after.Y) < 1e-9);
        Assert.Equal(8, ViewTransform.NextPreset(7));
        Assert.Equal(0.125, ViewTransform.PreviousPreset(0.25));
    }

    /// <summary>A-07: history memory budget eviction drops the oldest steps.</summary>
    [Fact]
    public void A07_HistoryBudgetEviction()
    {
        var doc = new PaintDocument(1024, 1024);
        doc.History.BudgetBytes = 3L * Tile.Count * 4 * 16; // room for ~3 full-canvas steps
        for (var i = 0; i < 10; i++)
        {
            var seg = doc.ActiveLayer.TopSegment.Pixels;
            for (var ty = 0; ty < 4; ty++)
            {
                for (var tx = 0; tx < 4; tx++)
                {
                    seg.GetWritableData(tx, ty)[0] = (uint)i + 1;
                }
            }

            doc.Commit($"step {i}");
        }

        Assert.True(doc.History.TotalBytes <= doc.History.BudgetBytes);
        Assert.True(doc.History.UndoCount < 10);
        Assert.True(doc.History.UndoCount >= 2);
        var undone = 0;
        while (doc.Undo())
        {
            undone++;
        }

        Assert.Equal(undone, doc.History.RedoCount);
    }

    [Fact]
    public void History_HoldsAtLeast100StepsByDefault()
    {
        var doc = new PaintDocument(1152, 648);
        for (var i = 0; i < 150; i++)
        {
            doc.ActiveLayer.TopSegment.Pixels.SetPixel(i, i, Rgb((byte)i, 0, 0));
            doc.Commit("dot");
        }

        Assert.Equal(150, doc.History.UndoCount);
    }

    [Fact]
    public void DirtyFlagTracksSavedState()
    {
        var doc = new PaintDocument(100, 100);
        Assert.False(doc.IsDirty);
        doc.ActiveLayer.TopSegment.Pixels.SetPixel(1, 1, 0xFF000000);
        doc.Commit("x");
        Assert.True(doc.IsDirty);
        doc.History.MarkSaved();
        Assert.False(doc.IsDirty);
        doc.Undo();
        Assert.True(doc.IsDirty);
        doc.Redo();
        Assert.False(doc.IsDirty);
    }
}
