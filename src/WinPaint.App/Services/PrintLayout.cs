using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WinPaint.Core.Imaging;

namespace WinPaint.App.Services;

/// <summary>Page setup values (inches for margins).</summary>
public sealed record PageSettings(bool Landscape, Thickness MarginsInches, int ScalePercent, int FitWide, int FitTall, bool CenterHorizontally, bool CenterVertically)
{
    /// <summary>Reads page settings from the app settings.</summary>
    public static PageSettings From(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var m = s.PrintMargins is { Length: 4 } a ? a : [0.75, 0.75, 0.75, 0.75];
        return new PageSettings(s.PrintLandscape, new Thickness(m[0], m[1], m[2], m[3]), Math.Clamp(s.PrintScalePercent, 1, 1000), s.PrintFitWide, s.PrintFitTall, s.PrintCenterH, s.PrintCenterV);
    }
}

/// <summary>Splits an image over printed pages according to page setup (scale or fit-to-pages, centering).</summary>
public sealed class PrintLayout
{
    private readonly BitmapSourceHolder _image;

    /// <summary>Computes the layout for a page size in DIPs (1/96 inch).</summary>
    public PrintLayout(PixelBuffer image, double dpiX, double dpiY, Size pageSize, PageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        _image = new BitmapSourceHolder(image.ToBitmapSource(dpiX, dpiY));
        PageSize = settings.Landscape && pageSize.Width < pageSize.Height ? new Size(pageSize.Height, pageSize.Width) : pageSize;
        var m = settings.MarginsInches;
        Content = new Rect(m.Left * 96, m.Top * 96, Math.Max(1, PageSize.Width - ((m.Left + m.Right) * 96)), Math.Max(1, PageSize.Height - ((m.Top + m.Bottom) * 96)));
        var w = image.Width / Math.Max(1, dpiX) * 96;
        var h = image.Height / Math.Max(1, dpiY) * 96;
        var scale = settings.FitWide > 0 && settings.FitTall > 0
            ? Math.Min(settings.FitWide * Content.Width / w, settings.FitTall * Content.Height / h)
            : settings.ScalePercent / 100.0;
        ImageSize = new Size(w * scale, h * scale);
        Columns = Math.Max(1, (int)Math.Ceiling((ImageSize.Width / Content.Width) - 1e-6));
        Rows = Math.Max(1, (int)Math.Ceiling((ImageSize.Height / Content.Height) - 1e-6));
        OffsetX = settings.CenterHorizontally && Columns == 1 ? (Content.Width - ImageSize.Width) / 2 : 0;
        OffsetY = settings.CenterVertically && Rows == 1 ? (Content.Height - ImageSize.Height) / 2 : 0;
    }

    /// <summary>Page size (DIPs) after orientation.</summary>
    public Size PageSize { get; }

    /// <summary>Printable content rectangle.</summary>
    public Rect Content { get; }

    /// <summary>Printed image size.</summary>
    public Size ImageSize { get; }

    /// <summary>Pages across.</summary>
    public int Columns { get; }

    /// <summary>Pages down.</summary>
    public int Rows { get; }

    /// <summary>Total pages.</summary>
    public int PageCount => Columns * Rows;

    /// <summary>Horizontal centering offset.</summary>
    public double OffsetX { get; }

    /// <summary>Vertical centering offset.</summary>
    public double OffsetY { get; }

    /// <summary>Drawing of one page (white paper, clipped image portion).</summary>
    public Drawing PageDrawing(int index, bool paper)
    {
        var col = index % Columns;
        var row = index / Columns;
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            if (paper)
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(PageSize));
            }

            dc.PushClip(new RectangleGeometry(Content));
            var x = Content.X + OffsetX - (col * Content.Width);
            var y = Content.Y + OffsetY - (row * Content.Height);
            dc.DrawImage(_image.Source, new Rect(x, y, ImageSize.Width, ImageSize.Height));
            dc.Pop();
        }

        g.Freeze();
        return g;
    }

    /// <summary>A paginator for the print system.</summary>
    public DocumentPaginator CreatePaginator() => new Paginator(this);

    private sealed record BitmapSourceHolder(ImageSource Source);

    private sealed class Paginator(PrintLayout layout) : DocumentPaginator
    {
        public override bool IsPageCountValid => true;

        public override int PageCount => layout.PageCount;

        public override Size PageSize
        {
            get => layout.PageSize;
            set
            {
            }
        }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawDrawing(layout.PageDrawing(pageNumber, paper: false));
            }

            return new DocumentPage(visual, layout.PageSize, new Rect(layout.PageSize), layout.Content);
        }
    }
}
