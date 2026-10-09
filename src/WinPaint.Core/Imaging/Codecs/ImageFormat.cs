namespace WinPaint.Core.Imaging.Codecs;

/// <summary>Supported file formats.</summary>
public enum ImageFormat
{
    /// <summary>Portable Network Graphics.</summary>
    Png,

    /// <summary>JPEG.</summary>
    Jpeg,

    /// <summary>Windows bitmap.</summary>
    Bmp,

    /// <summary>GIF (first frame on read).</summary>
    Gif,

    /// <summary>TIFF (first page on read).</summary>
    Tiff,

    /// <summary>Windows icon.</summary>
    Ico,
}

/// <summary>Extension ↔ format mapping and file dialog filters.</summary>
public static class ImageFormats
{
    private static readonly Dictionary<string, ImageFormat> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ImageFormat.Png,
        [".jpg"] = ImageFormat.Jpeg,
        [".jpeg"] = ImageFormat.Jpeg,
        [".jpe"] = ImageFormat.Jpeg,
        [".jfif"] = ImageFormat.Jpeg,
        [".bmp"] = ImageFormat.Bmp,
        [".dib"] = ImageFormat.Bmp,
        [".gif"] = ImageFormat.Gif,
        [".tif"] = ImageFormat.Tiff,
        [".tiff"] = ImageFormat.Tiff,
        [".ico"] = ImageFormat.Ico,
    };

    /// <summary>Open dialog filter covering every readable format.</summary>
    public const string OpenFilter =
        "All Picture Files|*.png;*.jpg;*.jpeg;*.jpe;*.jfif;*.bmp;*.dib;*.gif;*.tif;*.tiff;*.ico|" +
        "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg;*.jpe;*.jfif)|*.jpg;*.jpeg;*.jpe;*.jfif|Bitmap (*.bmp;*.dib)|*.bmp;*.dib|" +
        "GIF (*.gif)|*.gif|TIFF (*.tif;*.tiff)|*.tif;*.tiff|ICO (*.ico)|*.ico|All Files (*.*)|*.*";

    /// <summary>Save dialog filter (order matches <see cref="SaveFilterOrder"/>).</summary>
    public const string SaveFilter =
        "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg;*.jpe;*.jfif)|*.jpg;*.jpeg;*.jpe;*.jfif|24-bit Bitmap (*.bmp;*.dib)|*.bmp;*.dib|" +
        "GIF (*.gif)|*.gif|TIFF (*.tif;*.tiff)|*.tif;*.tiff|ICO (*.ico)|*.ico";

    /// <summary>Formats in the order of <see cref="SaveFilter"/>.</summary>
    public static IReadOnlyList<ImageFormat> SaveFilterOrder { get; } =
        [ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Bmp, ImageFormat.Gif, ImageFormat.Tiff, ImageFormat.Ico];

    /// <summary>Format for a path's extension, or null.</summary>
    public static ImageFormat? FromPath(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var f) ? f : null;

    /// <summary>Default extension for a format.</summary>
    public static string DefaultExtension(ImageFormat f) => f switch
    {
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Bmp => ".bmp",
        ImageFormat.Gif => ".gif",
        ImageFormat.Tiff => ".tif",
        ImageFormat.Ico => ".ico",
        _ => ".png",
    };
}
