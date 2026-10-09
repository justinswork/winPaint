using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPaint.Core.Tools;

namespace WinPaint.App.Controls;

/// <summary>Builds tool cursors from vector art at runtime (scaled for DPI) and caches them.</summary>
public static class CursorFactory
{
    /// <summary>Largest brush/eraser outline drawn as a cursor (device px); larger outlines are drawn as overlays.</summary>
    public const int MaxOutlineCursor = 128;

    private static readonly Dictionary<string, Cursor> Cache = [];

    /// <summary>Returns the cursor for a tool cursor kind.</summary>
    /// <param name="kind">Cursor kind.</param>
    /// <param name="outlineDevicePx">Brush/eraser outline size in device pixels.</param>
    /// <param name="dpiScale">Monitor scale.</param>
    public static Cursor Get(ToolCursor kind, double outlineDevicePx, double dpiScale)
    {
        switch (kind)
        {
            case ToolCursor.Arrow:
                return Cursors.Arrow;
            case ToolCursor.IBeam:
                return Cursors.IBeam;
            case ToolCursor.Move:
                return Cursors.SizeAll;
            case ToolCursor.SizeNwse:
                return Cursors.SizeNWSE;
            case ToolCursor.SizeNesw:
                return Cursors.SizeNESW;
            case ToolCursor.SizeWe:
                return Cursors.SizeWE;
            case ToolCursor.SizeNs:
                return Cursors.SizeNS;
        }

        var outline = (int)Math.Round(outlineDevicePx);
        if (kind is ToolCursor.BrushCircle or ToolCursor.EraserSquare && outline > MaxOutlineCursor)
        {
            kind = ToolCursor.Crosshair;
        }

        var key = $"{kind}|{(kind is ToolCursor.BrushCircle or ToolCursor.EraserSquare ? outline : 0)}|{dpiScale:F2}";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var cursor = Build(kind, Math.Max(3, outline), dpiScale);
        Cache[key] = cursor;
        return cursor;
    }

    private static Cursor Build(ToolCursor kind, int outline, double dpi)
    {
        var size = kind is ToolCursor.BrushCircle or ToolCursor.EraserSquare
            ? Math.Max((int)Math.Ceiling(outline + 4.0), (int)Math.Ceiling(16 * dpi))
            : (int)Math.Ceiling(32 * dpi);
        var visual = new DrawingVisual();
        var black = new Pen(Brushes.Black, 1.0 * dpi);
        var white = new Pen(Brushes.White, 3.0 * dpi);
        Point hot;
        using (var dc = visual.RenderOpen())
        {
            switch (kind)
            {
                case ToolCursor.BrushCircle:
                {
                    var c = new Point(size / 2.0, size / 2.0);
                    dc.DrawEllipse(null, new Pen(Brushes.White, 2), c, (outline / 2.0) + 0.5, (outline / 2.0) + 0.5);
                    dc.DrawEllipse(null, new Pen(Brushes.Black, 1), c, (outline / 2.0) + 0.5, (outline / 2.0) + 0.5);
                    hot = c;
                    break;
                }

                case ToolCursor.EraserSquare:
                {
                    var o = (size - outline) / 2.0;
                    var r = new Rect(o, o, outline, outline);
                    dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), r);
                    hot = new Point(size / 2.0, size / 2.0);
                    break;
                }

                case ToolCursor.Pencil:
                {
                    var g = Geometry.Parse("M1,31 L3,24 L22,5 L27,10 L8,29 Z M22,5 L25,2 L30,7 L27,10");
                    DrawArt(dc, g, dpi, Brushes.White, Brushes.Black);
                    hot = new Point(1 * dpi, 31 * dpi);
                    break;
                }

                case ToolCursor.Bucket:
                {
                    var g = Geometry.Parse("M14,4 L26,16 L16,26 L4,14 Z M8,10 L20,10 M26,16 C30,20 30,26 28,28 C26,26 26,20 26,16");
                    DrawArt(dc, g, dpi, Brushes.White, Brushes.Black);
                    hot = new Point(28 * dpi, 28 * dpi);
                    break;
                }

                case ToolCursor.Dropper:
                {
                    var g = Geometry.Parse("M1,31 L3,25 L18,10 L22,14 L7,29 Z M18,10 L22,6 C24,4 28,4 29,6 C31,8 29,11 26,14 L22,14");
                    DrawArt(dc, g, dpi, Brushes.White, Brushes.Black);
                    hot = new Point(1 * dpi, 31 * dpi);
                    break;
                }

                case ToolCursor.Magnifier:
                {
                    var g = Geometry.Parse("M12,2 A10,10 0 1 1 11.99,2 Z M19,19 L30,30 M8,12 L16,12 M12,8 L12,16");
                    DrawArt(dc, g, dpi, Brushes.White, Brushes.Black);
                    hot = new Point(12 * dpi, 12 * dpi);
                    break;
                }

                default:
                {
                    // Crosshair.
                    var c = size / 2.0;
                    foreach (var pen in new[] { white, black })
                    {
                        dc.DrawLine(pen, new Point(c, 1), new Point(c, c - (3 * dpi)));
                        dc.DrawLine(pen, new Point(c, c + (3 * dpi)), new Point(c, size - 1));
                        dc.DrawLine(pen, new Point(1, c), new Point(c - (3 * dpi), c));
                        dc.DrawLine(pen, new Point(c + (3 * dpi), c), new Point(size - 1, c));
                    }

                    hot = new Point(c, c);
                    break;
                }
            }
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return FromBitmap(rtb, (int)Math.Round(hot.X), (int)Math.Round(hot.Y));
    }

    private static void DrawArt(DrawingContext dc, Geometry g, double dpi, Brush fill, Brush stroke)
    {
        dc.PushTransform(new ScaleTransform(dpi, dpi));
        dc.DrawGeometry(null, new Pen(Brushes.White, 3.0) { LineJoin = PenLineJoin.Round }, g);
        dc.DrawGeometry(fill, new Pen(stroke, 1.2) { LineJoin = PenLineJoin.Round }, g);
        dc.Pop();
    }

    /// <summary>Creates a cursor from a premultiplied bitmap by writing a .cur image in memory.</summary>
    private static Cursor FromBitmap(BitmapSource bmp, int hotX, int hotY)
    {
        var w = bmp.PixelWidth;
        var h = bmp.PixelHeight;
        var px = new uint[w * h];
        bmp.CopyPixels(px, w * 4, 0);
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var maskStride = ((w + 31) / 32) * 4;
            var dibSize = 40 + (w * h * 4) + (maskStride * h);
            bw.Write((ushort)0);
            bw.Write((ushort)2);
            bw.Write((ushort)1);
            bw.Write((byte)(w >= 256 ? 0 : w));
            bw.Write((byte)(h >= 256 ? 0 : h));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)Math.Clamp(hotX, 0, w - 1));
            bw.Write((ushort)Math.Clamp(hotY, 0, h - 1));
            bw.Write(dibSize);
            bw.Write(22);
            bw.Write(40);
            bw.Write(w);
            bw.Write(h * 2);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            for (var y = h - 1; y >= 0; y--)
            {
                for (var x = 0; x < w; x++)
                {
                    bw.Write(Core.Imaging.ColorUtil.Unpremultiply(px[(y * w) + x]));
                }
            }

            for (var y = h - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (var x = 0; x < w; x++)
                {
                    if ((px[(y * w) + x] >> 24) == 0)
                    {
                        row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    }
                }

                bw.Write(row);
            }
        }

        ms.Position = 0;
        return new Cursor(ms, scaleWithDpi: false);
    }
}
