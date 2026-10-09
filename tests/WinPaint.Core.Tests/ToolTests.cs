using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.Core.Brushes;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;
using WinPaint.Core.Shapes;
using WinPaint.Core.Tools;
using static WinPaint.Core.Tests.TestUtil;

namespace WinPaint.Core.Tests;

/// <summary>General acceptance tests (§10) for tools and pixel operations.</summary>
public class ToolTests
{
    /// <summary>A-01: exact-match 4-connected fill, canvas edges, and 8 MP under 150 ms.</summary>
    [Fact]
    public void A01_FloodFill()
    {
        // 4-connectivity: diagonal neighbors are not connected.
        var b = new PixelBuffer(5, 5, ColorUtil.White);
        b[1, 1] = ColorUtil.Black;
        b[2, 2] = ColorUtil.Black;
        b[1, 2] = ColorUtil.Black;
        b[2, 1] = ColorUtil.Black;
        var (spans, bounds) = FloodFill.Region(b.Pixels, 5, 5, 1, 1);
        Assert.Equal(4, spans.Sum(s => s.X1 - s.X0 + 1));
        Assert.Equal(new PixelRect(1, 1, 2, 2), bounds);

        // Exact match: a 1-level different pixel is a border.
        var c = new PixelBuffer(10, 1, Rgb(10, 10, 10));
        c[5, 0] = Rgb(10, 10, 11);
        var (s2, _) = FloodFill.Region(c.Pixels, 10, 1, 0, 0);
        Assert.Equal(5, s2.Sum(s => s.X1 - s.X0 + 1));

        // Canvas edges.
        var (s3, b3) = FloodFill.Region(new PixelBuffer(7, 3).Pixels, 7, 3, 6, 2);
        Assert.Equal(21, s3.Sum(s => s.X1 - s.X0 + 1));
        Assert.Equal(new PixelRect(0, 0, 7, 3), b3);

        // Performance: 8 MP with obstacles.
        var big = new PixelBuffer(4000, 2000, ColorUtil.White);
        for (var x = 10; x < 4000; x += 37)
        {
            big.Fill(new PixelRect(x, 0, 2, 1900), ColorUtil.Black);
        }

        FloodFill.Region(big.Pixels, 4000, 2000, 0, 0);
        var sw = Stopwatch.StartNew();
        var (s4, _) = FloodFill.Region(big.Pixels, 4000, 2000, 0, 0);
        sw.Stop();
        Assert.True(s4.Count > 1000);
        RecordPerf("A-01-fill", $"8 MP (4000x2000) scanline flood fill: {sw.Elapsed.TotalMilliseconds:F1} ms");
        Assert.True(sw.ElapsedMilliseconds < 150, $"fill took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A01_FillToolUsesActiveLayerComposite() => Sta(() =>
    {
        var doc = new PaintDocument(100, 100);
        var host = new FakeHost(doc);
        doc.ActiveLayer.TopSegment.Pixels.FillRect(new PixelRect(0, 50, 100, 2), ColorUtil.Black);
        doc.Commit("line");
        host.Settings.Primary = Colors.Red;
        new FillTool(host).OnPointerDown(new PointerInput(new Point(5, 5), PointerButton.Left, ModifierKeys.None));
        Assert.Equal(ColorUtil.FromColor(Colors.Red), doc.CompositePixel(99, 0));
        Assert.Equal(ColorUtil.White, doc.CompositePixel(5, 80));
        Assert.Equal(2, doc.History.UndoCount);
    });

    /// <summary>A-02: each shape's geometry: bounds, closed path, point counts, Shift-constrained variant.</summary>
    [Fact]
    public void A02_ShapeGeometry() => Sta(() =>
    {
        var bounds = new Rect(10, 20, 200, 100);
        var expectedPoints = new Dictionary<ShapeKind, int>
        {
            [ShapeKind.Triangle] = 3,
            [ShapeKind.RightTriangle] = 3,
            [ShapeKind.Diamond] = 4,
            [ShapeKind.Pentagon] = 5,
            [ShapeKind.Hexagon] = 6,
            [ShapeKind.RightArrow] = 7,
            [ShapeKind.LeftArrow] = 7,
            [ShapeKind.UpArrow] = 7,
            [ShapeKind.DownArrow] = 7,
            [ShapeKind.FourPointStar] = 8,
            [ShapeKind.FivePointStar] = 10,
            [ShapeKind.SixPointStar] = 12,
            [ShapeKind.Lightning] = 11,
        };
        foreach (var kind in Enum.GetValues<ShapeKind>())
        {
            Geometry g = kind switch
            {
                ShapeKind.Line => ShapeGeometry.Line(bounds.TopLeft, bounds.BottomRight),
                ShapeKind.Curve => ShapeGeometry.Curve(bounds.TopLeft, bounds.BottomRight, new Point(50, 120), null),
                ShapeKind.Polygon => ShapeGeometry.FromPoints([new(10, 20), new(210, 20), new(110, 120)], true),
                _ => ShapeGeometry.Build(kind, bounds),
            };
            var gb = g.Bounds;
            Assert.False(gb.IsEmpty, kind.ToString());
            Assert.True(gb.Left >= bounds.Left - 0.01 && gb.Top >= bounds.Top - 25 && gb.Right <= bounds.Right + 0.01 && gb.Bottom <= bounds.Bottom + 0.01, $"{kind} bounds {gb}");
            var figures = ShapeGeometry.Flatten(g);
            Assert.NotEmpty(figures);
            if (!ShapeGeometry.IsOpen(kind))
            {
                Assert.True(figures.All(f => f.Closed), $"{kind} must be closed");
            }

            if (expectedPoints.TryGetValue(kind, out var n))
            {
                Assert.Equal(n, ShapeGeometry.PolygonPoints(kind, bounds).Count);
                Assert.True(Math.Abs(gb.Width - bounds.Width) < 0.01 && Math.Abs(gb.Height - bounds.Height) < 0.01, $"{kind} should fill its box");
            }
        }

        // Shift-constrained variants: square boxes and 45° lines.
        var sq = ShapeGeometry.DragBounds(new Point(0, 0), new Point(80, 30), constrain: true);
        Assert.Equal(sq.Width, sq.Height);
        var neg = ShapeGeometry.DragBounds(new Point(100, 100), new Point(40, 90), constrain: true);
        Assert.Equal(neg.Width, neg.Height);
        Assert.Equal(new Point(100, 0), ShapeGeometry.ConstrainLine(new Point(0, 0), new Point(100, 7)));
        var diag = ShapeGeometry.ConstrainLine(new Point(0, 0), new Point(50, 45));
        Assert.Equal(diag.X, diag.Y);
        foreach (var kind in expectedPoints.Keys)
        {
            var c = ShapeGeometry.Build(kind, sq).Bounds;
            Assert.True(Math.Abs(c.Width - c.Height) < 0.01, $"{kind} constrained");
        }
    });

    [Fact]
    public void A02_AllShapesRasterizeWithEveryStyle() => Sta(() =>
    {
        foreach (var kind in Enum.GetValues<ShapeKind>())
        {
            foreach (var style in Enum.GetValues<ShapeStyle>().Where(s => s != ShapeStyle.None))
            {
                var spec = new ShapeSpec
                {
                    Kind = kind,
                    Bounds = new Rect(10, 10, 80, 60),
                    Points = kind switch
                    {
                        ShapeKind.Line => [new(10, 10), new(90, 70)],
                        ShapeKind.Curve => [new(10, 70), new(90, 10), new(30, 0)],
                        ShapeKind.Polygon => [new(10, 10), new(90, 20), new(50, 70)],
                        _ => [],
                    },
                    Outline = style,
                    Fill = style,
                    Size = 4,
                    OutlineColor = Colors.Navy,
                    FillColor = Colors.Orange,
                };
                var r = ShapeRenderer.Render(spec, 100, 100);
                Assert.NotNull(r);
                Assert.Contains(r.Value.Pixels.Pixels, p => p != 0);
            }
        }
    });

    /// <summary>A-03: rotate 4× right equals the original; flipping twice equals the original.</summary>
    [Fact]
    public void A03_RotateFlipRoundTrips() => Sta(() =>
    {
        var img = RandomImage(37, 21, 9, opaque: false);
        var r = img;
        for (var i = 0; i < 4; i++)
        {
            r = Transforms.Apply(r, OrthoTransform.RotateRight);
        }

        Assert.True(img.ContentEquals(r));
        Assert.True(img.ContentEquals(Transforms.Apply(Transforms.Apply(img, OrthoTransform.FlipHorizontal), OrthoTransform.FlipHorizontal)));
        Assert.True(img.ContentEquals(Transforms.Apply(Transforms.Apply(img, OrthoTransform.FlipVertical), OrthoTransform.FlipVertical)));
        Assert.True(img.ContentEquals(Transforms.Apply(Transforms.Apply(img, OrthoTransform.RotateLeft), OrthoTransform.RotateRight)));

        // Whole-document rotation round trip (including the undo stack).
        var doc = PaintDocument.FromImage(img);
        for (var i = 0; i < 4; i++)
        {
            ImageOperations.Orthogonal(doc, OrthoTransform.RotateRight);
        }

        Assert.True(img.ContentEquals(doc.Flatten()));
    });

    /// <summary>A-06: undo/redo of every command type returns byte-identical document states.</summary>
    [Fact]
    public void A06_UndoRedoEveryCommandType() => Sta(() =>
    {
        var doc = new PaintDocument(300, 200);
        var host = new FakeHost(doc);
        var hashes = new List<string> { StateHash(doc) };
        void Step(Action a)
        {
            a();
            hashes.Add(StateHash(doc));
        }

        Step(() => FakeHost.Drag(new PencilTool(host), PointerButton.Left, new Point(5, 5), new Point(100, 50)));
        Step(() => FakeHost.Drag(new BrushTool(host), PointerButton.Right, new Point(10, 100), new Point(200, 120)));
        Step(() => FakeHost.Drag(new EraserTool(host), PointerButton.Left, new Point(50, 50), new Point(60, 150)));
        Step(() => new FillTool(host).OnPointerDown(new PointerInput(new Point(250, 10), PointerButton.Left, ModifierKeys.None)));
        Step(() =>
        {
            host.Settings.Shape = ShapeKind.Oval;
            var st = new ShapeTool(host);
            FakeHost.Drag(st, PointerButton.Left, new Point(20, 20), new Point(120, 90));
            st.CommitPending();
        });
        Step(() =>
        {
            var sel = new SelectionTool(host, false);
            FakeHost.Drag(sel, PointerButton.Left, new Point(10, 10), new Point(80, 80));
            FakeHost.Drag(sel, PointerButton.Left, new Point(40, 40), new Point(140, 90));
            sel.CommitPending();
        });
        Step(() =>
        {
            host.BeginNewText(new Rect(100, 100, 150, 40));
            host.Text.Session!.Update(t => t.Text = "Undo me");
            host.CommitTextEdit();
        });
        Step(() => ImageOperations.Orthogonal(doc, OrthoTransform.RotateRight));
        Step(() => ImageOperations.Resize(doc, 150, 250));
        Step(() => ImageOperations.Skew(doc, 10, 0, Colors.White));
        Step(() => ImageOperations.InvertColors(doc));
        Step(() => ImageOperations.ResizeCanvas(doc, 300, 300, Colors.White));
        Step(() => ImageOperations.Crop(doc, new PixelRect(5, 5, 200, 200)));
        Step(() => LayerOperations.Add(doc, "L2"));
        Step(() => FakeHost.Drag(new BrushTool(host), PointerButton.Left, new Point(10, 10), new Point(100, 100)));
        Step(() => LayerOperations.Duplicate(doc, 1, "L3"));
        Step(() => LayerOperations.Move(doc, 2, 1));
        Step(() => LayerOperations.MergeDown(doc, 2));
        Step(() => LayerOperations.Delete(doc, 1));
        Step(() => ImageOperations.BlackAndWhite(doc));
        Step(() => LayerOperations.FlattenImage(doc));

        for (var i = hashes.Count - 2; i >= 0; i--)
        {
            Assert.True(doc.Undo());
            Assert.Equal(hashes[i], StateHash(doc));
        }

        for (var i = 1; i < hashes.Count; i++)
        {
            Assert.True(doc.Redo());
            Assert.Equal(hashes[i], StateHash(doc));
        }
    });

    /// <summary>A-08: clipboard copy/paste round trip keeps alpha (PNG format).</summary>
    [Fact]
    public void A08_ClipboardKeepsAlpha() => Sta(() =>
    {
        var img = RandomImage(40, 30, 11, opaque: false);
        for (var i = 0; i < img.Pixels.Length; i++)
        {
            var s = ColorUtil.Unpremultiply(img.Pixels[i]);
            img.Pixels[i] = ColorUtil.Premultiply(ColorUtil.A(s), ColorUtil.R(s), ColorUtil.G(s), ColorUtil.B(s));
        }

        var data = ClipboardData.Create(img);
        Assert.True(ClipboardData.ContainsImage(data));
        var back = ClipboardData.Read(data);
        Assert.NotNull(back);
        Assert.True(img.ContentEquals(back));

        // DIB-only data is also readable.
        var dibOnly = new System.Windows.DataObject();
        dibOnly.SetData(System.Windows.DataFormats.Dib, new MemoryStream(ClipboardData.EncodeDib(img)));
        var fromDib = ClipboardData.Read(dibOnly);
        Assert.NotNull(fromDib);
        Assert.Equal(img.Width, fromDib.Width);
        Assert.Equal(img.Height, fromDib.Height);
    });

    /// <summary>A-10: transparent selection excludes pixels matching the secondary color.</summary>
    [Fact]
    public void A10_TransparentSelection() => Sta(() =>
    {
        var doc = new PaintDocument(100, 100);
        doc.ActiveLayer.TopSegment.Pixels.FillRect(new PixelRect(20, 20, 10, 10), ColorUtil.FromColor(Colors.Blue));
        doc.Commit("blue");
        var host = new FakeHost(doc);
        host.Settings.TransparentSelection = true;
        host.Settings.Secondary = Colors.White;
        var sel = new SelectionTool(host, false);
        FakeHost.Drag(sel, PointerButton.Left, new Point(10, 10), new Point(40, 40));
        FakeHost.Drag(sel, PointerButton.Left, new Point(15, 15), new Point(65, 15));
        var f = doc.Floating!;
        Assert.Equal(0u, f.Pixels[0, 0]);
        Assert.Equal(ColorUtil.FromColor(Colors.Blue), f.Pixels[15, 15]);
        sel.CommitPending();

        // The white surround of the moved selection did not cover what is beneath it.
        doc.ActiveLayer.TopSegment.Pixels.FillRect(new PixelRect(60, 60, 5, 5), ColorUtil.Black);
        Assert.Equal(ColorUtil.FromColor(Colors.Blue), doc.CompositePixel(75, 25));
    });

    /// <summary>A-11: Ctrl+drag duplicates; the original pixels stay in place.</summary>
    [Fact]
    public void A11_CtrlDragDuplicates() => Sta(() =>
    {
        var doc = new PaintDocument(100, 100);
        var red = ColorUtil.FromColor(Colors.Red);
        doc.ActiveLayer.TopSegment.Pixels.FillRect(new PixelRect(10, 10, 10, 10), red);
        doc.Commit("red");
        var host = new FakeHost(doc);
        var sel = new SelectionTool(host, false);
        FakeHost.Drag(sel, PointerButton.Left, new Point(10, 10), new Point(20, 20));
        FakeHost.Drag(sel, PointerButton.Left, ModifierKeys.Control, new Point(15, 15), new Point(55, 55));
        sel.CommitPending();
        Assert.Equal(red, doc.CompositePixel(15, 15));
        Assert.Equal(red, doc.CompositePixel(55, 55));
        Assert.Equal(ColorUtil.White, doc.CompositePixel(35, 35));
    });

    [Fact]
    public void SelectionOnlyOperationsInvertRotate() => Sta(() =>
    {
        var doc = new PaintDocument(100, 100);
        var host = new FakeHost(doc);
        var sel = new SelectionTool(host, false);
        FakeHost.Drag(sel, PointerButton.Left, new Point(10, 10), new Point(30, 20));
        Assert.True(sel.InvertSelectionColors());
        sel.CommitPending();
        Assert.Equal(ColorUtil.Black, doc.CompositePixel(15, 15));
        Assert.Equal(ColorUtil.White, doc.CompositePixel(50, 50));

        FakeHost.Drag(sel, PointerButton.Left, new Point(10, 10), new Point(30, 20));
        Assert.True(sel.TransformSelection(OrthoTransform.RotateRight));
        Assert.Equal(new PixelRect(15, 5, 10, 20), sel.SelectionBounds);
        sel.CommitPending();
        Assert.Equal(ColorUtil.Black, doc.CompositePixel(20, 22));
    });

    [Fact]
    public void ShiftDragStampsTrail() => Sta(() =>
    {
        var doc = new PaintDocument(200, 100);
        var red = ColorUtil.FromColor(Colors.Red);
        doc.ActiveLayer.TopSegment.Pixels.FillRect(new PixelRect(10, 10, 10, 10), red);
        var host = new FakeHost(doc);
        var sel = new SelectionTool(host, false);
        FakeHost.Drag(sel, PointerButton.Left, new Point(10, 10), new Point(20, 20));
        // A real drag delivers many small moves; each one stamps the floating pixels.
        FakeHost.Drag(sel, PointerButton.Left, ModifierKeys.Shift, [.. Enumerable.Range(0, 22).Select(i => new Point(15 + (i * 5), 15))]);
        sel.CommitPending();
        Assert.Equal(red, doc.CompositePixel(52, 12));
        Assert.Equal(red, doc.CompositePixel(110, 12));
    });

    [Fact]
    public void PencilDrawsAliasedAndShiftConstrains() => Sta(() =>
    {
        var doc = new PaintDocument(100, 100);
        var host = new FakeHost(doc);
        FakeHost.Drag(new PencilTool(host), PointerButton.Left, ModifierKeys.Shift, new Point(10.5, 10.5), new Point(60.5, 13.5));
        var flat = doc.Flatten();
        Assert.Equal(ColorUtil.Black, flat[40, 10]);
        Assert.Equal(ColorUtil.White, flat[40, 12]);
        Assert.All(flat.Pixels, p => Assert.True(p is ColorUtil.Black or ColorUtil.White));
        Assert.Equal(PencilTool.Constrain((0, 0), (10, 9)), (10, 10));
        Assert.Equal(PencilTool.Constrain((0, 0), (10, 1)), (10, 0));
    });

    [Fact]
    public void EraserOpaqueVsTransparentVsColorReplace() => Sta(() =>
    {
        var doc = new PaintDocument(100, 100);
        var host = new FakeHost(doc);
        host.Settings.Secondary = Colors.Yellow;
        host.Settings.EraserSize = 6;
        doc.ActiveLayer.TopSegment.Pixels.FillRect(new PixelRect(0, 0, 100, 50), ColorUtil.FromColor(Colors.Red));
        FakeHost.Drag(new EraserTool(host), PointerButton.Left, new Point(10, 70), new Point(90, 70));
        Assert.Equal(ColorUtil.FromColor(Colors.Yellow), doc.CompositePixel(50, 70));

        host.Settings.Primary = Colors.Red;
        host.Settings.Secondary = Colors.Lime;
        FakeHost.Drag(new EraserTool(host), PointerButton.Right, new Point(10, 48), new Point(90, 48));
        Assert.Equal(ColorUtil.FromColor(Colors.Lime), doc.CompositePixel(50, 46));
        Assert.Equal(ColorUtil.FromColor(Colors.Red), doc.CompositePixel(50, 40));
        Assert.Equal(ColorUtil.White, doc.CompositePixel(50, 51));

        var t = new PaintDocument(50, 50, 0);
        var th = new FakeHost(t);
        t.ActiveLayer.TopSegment.Pixels.FillRect(t.Bounds, ColorUtil.Black);
        FakeHost.Drag(new EraserTool(th), PointerButton.Left, new Point(25, 5), new Point(25, 45));
        Assert.Equal(0u, t.CompositePixel(25, 25));
    });

    [Fact]
    public void AllBrushesPaintAndAreDeterministic() => Sta(() =>
    {
        foreach (var kind in Enum.GetValues<BrushKind>())
        {
            string Run()
            {
                var c = new StrokeCanvas(120, 60);
                var e = BrushEngine.Create(kind, Colors.Purple, 10, 42);
                e.Begin(c, new Point(10, 30));
                e.MoveTo(c, new Point(110, 35));
                e.Tick(c);
                return c.Surface.ToPixelBuffer().ContentHash();
            }

            var a = Run();
            Assert.Equal(a, Run());
            var c2 = new StrokeCanvas(120, 60);
            var e2 = BrushEngine.Create(kind, Colors.Purple, 10, 42);
            e2.Begin(c2, new Point(10, 30));
            e2.MoveTo(c2, new Point(110, 35));
            Assert.True(c2.TotalBounds.Width > 80, $"{kind} should cover the stroke");
        }
    });

    [Fact]
    public void FastStrokesHaveNoGaps() => Sta(() =>
    {
        var c = new StrokeCanvas(400, 50);
        var e = BrushEngine.Create(BrushKind.Brush, Colors.Black, 6, 1);
        e.Begin(c, new Point(5, 25));
        e.MoveTo(c, new Point(395, 25));
        for (var x = 5; x < 395; x++)
        {
            Assert.True(ColorUtil.A(c.Surface.GetPixel(x, 25)) > 200, $"gap at {x}");
        }
    });

    [Fact]
    public void MarkerDoesNotAccumulateWithinStroke() => Sta(() =>
    {
        var doc = new PaintDocument(200, 100);
        var host = new FakeHost(doc);
        host.Settings.Brush = BrushKind.Marker;
        host.Settings.BrushSize = 20;
        host.Settings.Primary = Colors.Black;
        FakeHost.Drag(new BrushTool(host), PointerButton.Left, new Point(20, 50), new Point(180, 50), new Point(20, 50), new Point(180, 50));
        var v1 = doc.CompositePixel(100, 50);
        Assert.InRange(ColorUtil.R(v1), 100, 160);
    });

    [Fact]
    public void OpacityAppliesToStroke() => Sta(() =>
    {
        var doc = new PaintDocument(100, 100);
        var host = new FakeHost(doc);
        host.Settings.Opacity = 0.5;
        host.Settings.PencilSize = 5;
        FakeHost.Drag(new PencilTool(host), PointerButton.Left, new Point(10, 50), new Point(90, 50), new Point(10, 50));
        var p = doc.CompositePixel(50, 50);
        Assert.InRange(ColorUtil.R(p), 120, 135);
    });

    [Fact]
    public void ShapeToolFloatsAndAdjustsThenCommits() => Sta(() =>
    {
        var doc = new PaintDocument(200, 200);
        var host = new FakeHost(doc);
        host.Settings.Shape = ShapeKind.Rectangle;
        host.Settings.ShapeSize = 3;
        var st = new ShapeTool(host);
        FakeHost.Drag(st, PointerButton.Left, new Point(20, 20), new Point(80, 60));
        Assert.True(st.IsAdjusting);
        Assert.Equal(0, doc.History.UndoCount);
        Assert.NotNull(doc.Floating);

        // Restyle live, then move by dragging inside, then commit with a click outside.
        host.Settings.Fill = ShapeStyle.Solid;
        host.Settings.Secondary = Colors.Green;
        st.RefreshFromSettings();
        FakeHost.Drag(st, PointerButton.Left, new Point(50, 40), new Point(110, 120));
        Assert.Equal(new Rect(80, 100, 60, 40), st.AdjustBounds);
        st.OnPointerDown(new PointerInput(new Point(5, 190), PointerButton.Left, ModifierKeys.None));
        st.OnPointerUp(new PointerInput(new Point(5, 190), PointerButton.Left, ModifierKeys.None));
        Assert.Equal(1, doc.History.UndoCount);
        Assert.Equal(ColorUtil.FromColor(Colors.Green), doc.CompositePixel(110, 120));
        Assert.Equal(ColorUtil.Black, doc.CompositePixel(80, 120));
    });

    [Fact]
    public void CurveAndPolygonInteractions() => Sta(() =>
    {
        var doc = new PaintDocument(200, 200);
        var host = new FakeHost(doc);
        host.Settings.Shape = ShapeKind.Curve;
        var st = new ShapeTool(host);
        FakeHost.Drag(st, PointerButton.Left, new Point(20, 100), new Point(180, 100));
        FakeHost.Drag(st, PointerButton.Left, new Point(60, 20), new Point(60, 10));
        FakeHost.Drag(st, PointerButton.Left, new Point(140, 190), new Point(140, 190));
        Assert.True(st.IsAdjusting);
        st.CommitPending();
        Assert.Equal(1, doc.History.UndoCount);

        host.Settings.Shape = ShapeKind.Polygon;
        FakeHost.Drag(st, PointerButton.Left, new Point(20, 20), new Point(100, 20));
        st.OnPointerDown(new PointerInput(new Point(100, 100), PointerButton.Left, ModifierKeys.None));
        st.OnPointerUp(new PointerInput(new Point(100, 100), PointerButton.Left, ModifierKeys.None));
        st.OnPointerDown(new PointerInput(new Point(20, 20), PointerButton.Left, ModifierKeys.None));
        st.OnPointerUp(new PointerInput(new Point(20, 20), PointerButton.Left, ModifierKeys.None));
        Assert.True(st.IsAdjusting);
        st.CommitPending();
        Assert.Equal(2, doc.History.UndoCount);
    });

    [Fact]
    public void PolygonKeepsEveryClickedVertex() => Sta(() =>
    {
        var doc = new PaintDocument(1152, 648);
        var host = new FakeHost(doc);
        host.Settings.Shape = ShapeKind.Polygon;
        var st = new ShapeTool(host);
        void Click(double x, double y, int count = 1)
        {
            st.OnPointerDown(new PointerInput(new Point(x, y), PointerButton.Left, ModifierKeys.None, count));
            st.OnPointerUp(new PointerInput(new Point(x, y), PointerButton.Left, ModifierKeys.None, count));
        }

        FakeHost.Drag(st, PointerButton.Left, new Point(955, 145), new Point(990, 85), new Point(1025, 25));
        Click(1105, 145);
        Click(1030, 115);
        Click(1030, 115);
        Click(1030, 115, 2);
        st.CommitPending();
        var flat = doc.Flatten();
        Assert.NotEqual(ColorUtil.White, flat[1104, 144]);
        Assert.Equal(ColorUtil.White, flat[400, 150]);
    });

    [Fact]
    public void PickerAndMagnifier() => Sta(() =>
    {
        var doc = new PaintDocument(50, 50);
        doc.ActiveLayer.TopSegment.Pixels.SetPixel(5, 5, ColorUtil.FromColor(Colors.Teal));
        var host = new FakeHost(doc);
        new PickerTool(host).OnPointerDown(new PointerInput(new Point(5.5, 5.5), PointerButton.Right, ModifierKeys.None));
        Assert.Equal(Colors.Teal, host.Settings.Secondary);
        Assert.Equal(1, host.RestoreCount);
        var mag = new MagnifierTool(host);
        mag.OnPointerDown(new PointerInput(new Point(10, 10), PointerButton.Left, ModifierKeys.None));
        mag.OnPointerDown(new PointerInput(new Point(10, 10), PointerButton.Right, ModifierKeys.None));
        Assert.Equal([(new Point(10, 10), true), (new Point(10, 10), false)], host.Zooms);
    });

    [Fact]
    public void LargeImage10000Works() => Sta(() =>
    {
        var doc = new PaintDocument(10000, 10000);
        var host = new FakeHost(doc);
        FakeHost.Drag(new BrushTool(host), PointerButton.Left, new Point(100, 100), new Point(9900, 9900));
        Assert.Equal(ColorUtil.Black, doc.CompositePixel(5000, 5000));
        doc.Undo();
        Assert.Equal(ColorUtil.White, doc.CompositePixel(5000, 5000));
    });

    internal static string StateHash(PaintDocument doc)
    {
        var parts = new List<string> { doc.Flatten().ContentHash(), $"{doc.Width}x{doc.Height}", doc.ActiveLayerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        foreach (var l in doc.Layers)
        {
            parts.Add($"{l.Id}:{l.Visible}:{l.Opacity}:{l.BlendMode}:{l.Elements.Count}");
            parts.AddRange(l.TextObjects.Select(t => t.RenderKey()));
            parts.AddRange(l.Elements.OfType<RasterSegment>().Select(s => s.Pixels.ToPixelBuffer().ContentHash()));
        }

        return string.Join("|", parts);
    }
}
