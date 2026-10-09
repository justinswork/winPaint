using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;
using WinPaint.Core.Shapes;
using WinPaint.Core.Tools;
using Xunit.Abstractions;
using static WinPaint.Core.Tests.TestUtil;

namespace WinPaint.Core.Tests;

/// <summary>Additional feature coverage for selection, shapes, clipboard and rendering performance.</summary>
public class MoreCoreTests(ITestOutputHelper output)
{
    /// <summary>F-EDIT-03: clipboard text is never turned into an image or a text object.</summary>
    [Fact]
    public void ClipboardTextIsIgnoredForImages() => Sta(() =>
    {
        var data = new DataObject();
        data.SetText("just text");
        Assert.False(ClipboardData.ContainsImage(data));
        Assert.Null(ClipboardData.Read(data));
    });

    /// <summary>F-SEL-07: cropping a free-form selection fills the outside of the shape with the secondary color.</summary>
    [Fact]
    public void FreeFormCropFillsOutsideWithSecondary() => Sta(() =>
    {
        var doc = new PaintDocument(200, 200);
        doc.ActiveLayer.TopSegment.Pixels.FillRect(doc.Bounds, ColorUtil.FromColor(Colors.Blue));
        doc.Commit("blue");
        var host = new FakeHost(doc);
        host.Settings.Secondary = Colors.Yellow;
        var sel = new SelectionTool(host, freeForm: true);
        FakeHost.Drag(sel, PointerButton.Left, new Point(50, 50), new Point(150, 50), new Point(100, 150), new Point(50, 50));
        Assert.True(sel.CropToSelection());
        Assert.Equal(100, doc.Width);
        Assert.Equal(ColorUtil.FromColor(Colors.Blue), doc.CompositePixel(50, 30));
        Assert.Equal(ColorUtil.FromColor(Colors.Yellow), doc.CompositePixel(3, 90));
        Assert.Equal(2, doc.History.UndoCount);
        doc.Undo();
        Assert.Equal(200, doc.Width);
    });

    /// <summary>F-SH-05: a right-drag swaps outline (secondary) and fill (primary) colors.</summary>
    [Fact]
    public void ShapeRightDragSwapsColors() => Sta(() =>
    {
        var doc = new PaintDocument(200, 200);
        var host = new FakeHost(doc);
        host.Settings.Primary = Colors.Red;
        host.Settings.Secondary = Colors.Lime;
        host.Settings.Shape = ShapeKind.Rectangle;
        host.Settings.Fill = ShapeStyle.Solid;
        host.Settings.ShapeSize = 4;
        var st = new ShapeTool(host);
        FakeHost.Drag(st, PointerButton.Right, new Point(20, 20), new Point(180, 180));
        st.CommitPending();
        Assert.Equal(ColorUtil.FromColor(Colors.Red), doc.CompositePixel(100, 100));
        Assert.Equal(ColorUtil.FromColor(Colors.Lime), doc.CompositePixel(20, 100));
    });

    /// <summary>F-SEL-01: Shift constrains to a square; handles resize (resample) the lifted pixels.</summary>
    [Fact]
    public void RectSelectionShiftSquareAndHandleResize() => Sta(() =>
    {
        var doc = new PaintDocument(300, 300);
        doc.ActiveLayer.TopSegment.Pixels.FillRect(new PixelRect(10, 10, 20, 20), ColorUtil.Black);
        var host = new FakeHost(doc);
        var sel = new SelectionTool(host, false);
        FakeHost.Drag(sel, PointerButton.Left, ModifierKeys.Shift, new Point(10, 10), new Point(50, 30));
        Assert.Equal(new PixelRect(10, 10, 40, 40), sel.SelectionBounds);
        sel.Deselect();
        FakeHost.Drag(sel, PointerButton.Left, new Point(10, 10), new Point(30, 30));
        FakeHost.Drag(sel, PointerButton.Left, new Point(30, 30), new Point(70, 70));
        Assert.Equal(new PixelRect(10, 10, 60, 60), sel.SelectionBounds);
        sel.CommitPending();
        Assert.Equal(ColorUtil.Black, doc.CompositePixel(65, 65));
        Assert.Equal(ColorUtil.White, doc.CompositePixel(75, 75));
    });

    /// <summary>F-SEL-03: invert selection selects everything outside; delete clears it.</summary>
    [Fact]
    public void InvertSelectionAndDelete() => Sta(() =>
    {
        var doc = new PaintDocument(100, 100);
        doc.ActiveLayer.TopSegment.Pixels.FillRect(doc.Bounds, ColorUtil.Black);
        var host = new FakeHost(doc);
        var sel = new SelectionTool(host, false);
        FakeHost.Drag(sel, PointerButton.Left, new Point(25, 25), new Point(75, 75));
        sel.InvertSelection();
        Assert.True(sel.DeleteSelection());
        Assert.Equal(ColorUtil.White, doc.CompositePixel(5, 5));
        Assert.Equal(ColorUtil.Black, doc.CompositePixel(50, 50));
    });

    /// <summary>F-VIEW-08: a brush stroke on a 3840×2160 canvas updates (stroke + composite of the dirty rect) in &lt;16 ms per move.</summary>
    [Fact]
    public void StrokeRenderingIsFastAt4K() => Sta(() =>
    {
        var doc = new PaintDocument(3840, 2160);
        var host = new FakeHost(doc);
        host.Settings.BrushSize = 20;
        var dirty = PixelRect.Empty;
        doc.Invalidated += (_, r) => dirty = dirty.Union(r);
        var buffer = new uint[3840 * 2160];
        var tool = new BrushTool(host);
        var sw = new Stopwatch();
        var moves = 0;
        tool.OnPointerDown(new PointerInput(new Point(100, 100), PointerButton.Left, ModifierKeys.None));
        for (var i = 1; i <= 120; i++)
        {
            dirty = PixelRect.Empty;
            sw.Start();
            tool.OnPointerMove(new PointerInput(new Point(100 + (i * 30), 100 + (Math.Sin(i / 5.0) * 600) + 800), PointerButton.Left, ModifierKeys.None));
            if (!dirty.IsEmpty)
            {
                doc.RenderComposite(dirty, buffer, 0, dirty.Width);
            }

            sw.Stop();
            moves++;
        }

        tool.OnPointerUp(new PointerInput(new Point(3700, 900), PointerButton.Left, ModifierKeys.None));
        var avg = sw.Elapsed.TotalMilliseconds / moves;
        output.WriteLine($"Average brush move at 4K: {avg:F2} ms");
        RecordPerf("F-VIEW-08-stroke", $"3840x2160 brush stroke (size 20): {avg:F2} ms per pointer move including compositing the dirty rectangle");
        Assert.True(avg < 16, $"average {avg:F1} ms per move");
    });
}
