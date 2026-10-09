using System.Windows.Media.Imaging;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;
using static WinPaint.Core.Tests.TestUtil;

namespace WinPaint.Core.Tests;

public class ImagingTests
{
    [Fact]
    public void TiledSurface_SnapshotIsCopyOnWrite()
    {
        var s = new TiledSurface(600, 300);
        s.SetPixel(10, 10, Rgb(255, 0, 0));
        var snap = s.Snapshot();
        s.SetPixel(10, 10, Rgb(0, 255, 0));
        s.SetPixel(500, 200, Rgb(0, 0, 255));
        Assert.Equal(Rgb(255, 0, 0), snap.GetPixel(10, 10));
        Assert.Equal(0u, snap.GetPixel(500, 200));
        Assert.Equal(Rgb(0, 255, 0), s.GetPixel(10, 10));
    }

    [Fact]
    public void TiledSurface_RoundTripsThroughPixelBuffer()
    {
        var img = RandomImage(700, 333, 1, opaque: false);
        var s = TiledSurface.FromPixelBuffer(img);
        Assert.True(img.ContentEquals(s.ToPixelBuffer()));
        var part = s.ToPixelBuffer(new PixelRect(250, 100, 300, 200));
        Assert.True(img.Crop(new PixelRect(250, 100, 300, 200)).ContentEquals(part));
    }

    [Fact]
    public void TiledSurface_UniformFillHasNoStorage()
    {
        var s = TiledSurface.CreateFilled(10000, 10000, ColorUtil.White);
        Assert.All(s.AllocatedTiles, t => Assert.True(t.IsUniform));
        Assert.Equal(ColorUtil.White, s.GetPixel(9999, 9999));
    }

    [Fact]
    public void Over_MatchesFormula()
    {
        var src = ColorUtil.Premultiply(128, 255, 0, 0);
        var dst = Rgb(0, 0, 255);
        var o = ColorUtil.Over(src, dst);
        Assert.Equal(255, ColorUtil.A(o));
        Assert.InRange(ColorUtil.R(o), 127, 129);
        Assert.InRange(ColorUtil.B(o), 126, 128);
    }

    /// <summary>A-09: blend modes match reference formulas for sample pixels.</summary>
    [Theory]
    [InlineData(BlendMode.Multiply)]
    [InlineData(BlendMode.Screen)]
    [InlineData(BlendMode.Overlay)]
    [InlineData(BlendMode.Darken)]
    [InlineData(BlendMode.Lighten)]
    [InlineData(BlendMode.Difference)]
    [InlineData(BlendMode.Normal)]
    public void A09_BlendModesMatchReference(BlendMode mode)
    {
        byte[] samples = [0, 30, 100, 128, 200, 255];
        foreach (var s in samples)
        {
            foreach (var d in samples)
            {
                var r = Blend.Pixel(mode, Rgb(s, d, 77), Rgb(d, s, 200), 255);
                Assert.Equal(255, ColorUtil.A(r));
                Assert.InRange(ColorUtil.R(r), Ref(mode, s, d) - 1, Ref(mode, s, d) + 1);
                Assert.InRange(ColorUtil.G(r), Ref(mode, d, s) - 1, Ref(mode, d, s) + 1);
                Assert.InRange(ColorUtil.B(r), Ref(mode, 77, 200) - 1, Ref(mode, 77, 200) + 1);
            }
        }

        static int Ref(BlendMode m, int cs, int cb)
        {
            double s = cs / 255.0, b = cb / 255.0;
            var v = m switch
            {
                BlendMode.Multiply => s * b,
                BlendMode.Screen => s + b - (s * b),
                BlendMode.Overlay => b <= 0.5 ? 2 * s * b : 1 - (2 * (1 - s) * (1 - b)),
                BlendMode.Darken => Math.Min(s, b),
                BlendMode.Lighten => Math.Max(s, b),
                BlendMode.Difference => Math.Abs(s - b),
                _ => s,
            };
            return (int)Math.Round(v * 255);
        }
    }

    [Fact]
    public void A09_BlendRespectsOpacityAndTransparency()
    {
        var r = Blend.Pixel(BlendMode.Multiply, Rgb(255, 255, 255), 0, 255);
        Assert.Equal(Rgb(255, 255, 255), r);
        var half = Blend.Pixel(BlendMode.Normal, Rgb(255, 0, 0), Rgb(0, 0, 255), 128);
        Assert.InRange(ColorUtil.R(half), 127, 129);
    }

    /// <summary>A-04: resize 100% is a no-op and integer nearest upscale is exact.</summary>
    [Fact]
    public void A04_Resize()
    {
        var img = RandomImage(37, 23, 2);
        Assert.True(img.ContentEquals(Resampler.ResizeAuto(img, 37, 23)));
        var up = Resampler.ResizeAuto(img, 74, 69);
        for (var y = 0; y < 69; y++)
        {
            for (var x = 0; x < 74; x++)
            {
                Assert.Equal(img[x / 2, y / 3], up[x, y]);
            }
        }

        var down = Resampler.Resize(img, 10, 7, ResampleMode.HighQuality);
        Assert.Equal(10, down.Width);
        Assert.Equal(7, down.Height);
    }

    /// <summary>A-05: codec round trips.</summary>
    [Theory]
    [InlineData(ImageFormat.Png)]
    [InlineData(ImageFormat.Bmp)]
    [InlineData(ImageFormat.Tiff)]
    public void A05_LosslessRoundTrip(ImageFormat format) => Sta(() =>
    {
        var img = RandomImage(64, 48, 3, opaque: false);

        // Lossless means equal after premultiply → straight → premultiply; normalize the reference the same way.
        for (var i = 0; i < img.Pixels.Length; i++)
        {
            var s = ColorUtil.Unpremultiply(img.Pixels[i]);
            img.Pixels[i] = ColorUtil.Premultiply(ColorUtil.A(s), ColorUtil.R(s), ColorUtil.G(s), ColorUtil.B(s));
        }

        var path = TempFile("img" + ImageFormats.DefaultExtension(format));
        ImageCodec.Encode(img, path, format);
        var back = ImageCodec.Decode(path).Pixels;
        Assert.True(img.ContentEquals(back), $"{format} round trip differs");

        var opaque = RandomImage(31, 17, 4);
        ImageCodec.Encode(opaque, path, format);
        Assert.True(opaque.ContentEquals(ImageCodec.Decode(path).Pixels));
    });

    [Fact]
    public void A05_JpegDecodesWithExpectedDimensions() => Sta(() =>
    {
        var path = TempFile("a.jpg");
        ImageCodec.Encode(RandomImage(123, 45, 5, opaque: false), path, ImageFormat.Jpeg);
        var d = ImageCodec.Decode(path);
        Assert.Equal(123, d.Pixels.Width);
        Assert.Equal(45, d.Pixels.Height);
        Assert.False(d.Pixels.HasTransparency());
    });

    [Fact]
    public void A05_GifHasAtMost256ColorsAndBinaryTransparency() => Sta(() =>
    {
        var img = RandomImage(97, 61, 6);
        img.Fill(new PixelRect(0, 0, 10, 10), 0);
        var path = TempFile("a.gif");
        ImageCodec.Encode(img, path, ImageFormat.Gif);
        var d = ImageCodec.Decode(path).Pixels;
        Assert.Equal(97, d.Width);
        Assert.True(d.Pixels.Distinct().Count() <= 256);
        Assert.Equal(0, ColorUtil.A(d[5, 5]));
        Assert.Equal(255, ColorUtil.A(d[50, 50]));
    });

    [Fact]
    public void A05_GifWithFewColorsIsExact() => Sta(() =>
    {
        var img = new PixelBuffer(300, 200, Rgb(10, 20, 30));
        img.Fill(new PixelRect(50, 50, 100, 100), Rgb(200, 100, 0));
        var path = TempFile("b.gif");
        ImageCodec.Encode(img, path, ImageFormat.Gif);
        Assert.True(img.ContentEquals(ImageCodec.Decode(path).Pixels));
    });

    [Fact]
    public void A05_IcoContainsRequestedSizes() => Sta(() =>
    {
        var img = RandomImage(300, 200, 7);
        var path = TempFile("a.ico");
        ImageCodec.Encode(img, path, ImageFormat.Ico, new EncodeOptions { IcoSizes = [16, 32, 48, 256] });
        using var fs = File.OpenRead(path);
        var dec = new IconBitmapDecoder(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var sizes = dec.Frames.Select(f => f.PixelWidth).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 16, 32, 48, 256 }, sizes);
        Assert.All(dec.Frames, f => Assert.Equal(f.PixelWidth, f.PixelHeight));
        Assert.Equal(256, ImageCodec.Decode(path).Pixels.Width);
    });

    [Fact]
    public void Decode_CorruptFileThrowsFriendlyException() => Sta(() =>
    {
        var path = TempFile("bad.png");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        Assert.Throws<ImageOpenException>(() => ImageCodec.Decode(path));
        Assert.Throws<ImageOpenException>(() => ImageCodec.Decode(TempFile("missing.png")));
    });
}
