using WinPaint.Core.History;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.Core.Projects;

/// <summary>What happened to a project embedded in an opened image (WPP spec §6.5, §7.2).</summary>
public enum EmbeddedProjectStatus
{
    /// <summary>The file has no embedded project.</summary>
    None,

    /// <summary>The project matched the image and was restored.</summary>
    Restored,

    /// <summary>The image was changed outside winPaint; the project is available but wasn't restored.</summary>
    ChangedOutside,

    /// <summary>The embedded project was damaged or unsupported and was ignored.</summary>
    Ignored,
}

/// <summary>The result of opening a file.</summary>
/// <param name="Image">The decoded image (null for a .wpp file).</param>
/// <param name="Project">The project, when one was found and could be read.</param>
/// <param name="Status">For images: what happened to the embedded project.</param>
/// <param name="IgnoredReason">Why an embedded project was ignored.</param>
public sealed record OpenedFile(DecodedImage? Image, ProjectReadResult? Project, EmbeddedProjectStatus Status, string? IgnoredReason)
{
    /// <summary>True when the document should be built from <see cref="Project"/>.</summary>
    public bool UseProject => Project is not null && (Image is null || Status == EmbeddedProjectStatus.Restored);
}

/// <summary>Opens and saves projects as .wpp files and inside images.</summary>
public static class ProjectFiles
{
    /// <summary>True for a .wpp path.</summary>
    public static bool IsProjectPath(string path) =>
        string.Equals(Path.GetExtension(path), ProjectFormat.FileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens a .wpp file or an image (restoring an embedded project when it matches).</summary>
    /// <exception cref="ImageOpenException">The file can't be read.</exception>
    public static OpenedFile Open(string path)
    {
        byte[] bytes;
        try
        {
            if (new FileInfo(path).Length > ProjectFormat.MaxEmbeddedPayload)
            {
                // Too large to hold in memory as one array: open as a plain image.
                return IsProjectPath(path)
                    ? throw new ImageOpenException("This project is too large to open.")
                    : new OpenedFile(ImageCodec.Decode(path), null, EmbeddedProjectStatus.None, null);
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ImageOpenException($"Access to \"{Path.GetFileName(path)}\" was denied.", ex);
        }
        catch (IOException ex)
        {
            throw new ImageOpenException($"\"{Path.GetFileName(path)}\" could not be read: {ex.Message}", ex);
        }

        return IsProjectPath(path) ? OpenProject(bytes) : OpenImage(bytes);
    }

    /// <summary>Reads a .wpp file's bytes.</summary>
    public static OpenedFile OpenProject(byte[] bytes)
    {
        try
        {
            return new OpenedFile(null, ProjectReader.Read(bytes), EmbeddedProjectStatus.None, null);
        }
        catch (ProjectFormatException ex)
        {
            throw new ImageOpenException(ex.Message, ex);
        }
    }

    /// <summary>Decodes an image's bytes and checks for an embedded project.</summary>
    public static OpenedFile OpenImage(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var image = ImageCodec.Decode(new MemoryStream(bytes, writable: false));
        ProjectReadResult project;
        try
        {
            var container = ProjectEmbedding.Extract(bytes);
            if (container is null)
            {
                return new OpenedFile(image, null, EmbeddedProjectStatus.None, null);
            }

            project = ProjectReader.Read(container);
        }
        catch (ProjectFormatException ex)
        {
            return new OpenedFile(image, null, EmbeddedProjectStatus.Ignored, ex.Message);
        }

        var match = project.HostFingerprint is not null && project.HostFingerprint == ProjectImages.Fingerprint(image.Pixels);
        return new OpenedFile(image, project, match ? EmbeddedProjectStatus.Restored : EmbeddedProjectStatus.ChangedOutside, null);
    }

    /// <summary>
    /// Encodes the flattened image; when <paramref name="project"/> is given and the format can carry one, the
    /// project is embedded with the fingerprint of the encoded result.
    /// </summary>
    public static byte[] EncodeImage(PixelBuffer flat, ImageFormat format, EncodeOptions? options, DocumentState? project, ProjectWriteOptions? writeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(flat);
        byte[] encoded;
        using (var ms = new MemoryStream())
        {
            ImageCodec.Encode(flat, ms, format, options);
            encoded = ms.ToArray();
        }

        if (project is null || !ProjectEmbedding.CanEmbed(format))
        {
            return encoded;
        }

        // Fingerprint what a reader will decode (lossy JPEG and quantized GIF included).
        var decoded = ImageCodec.Decode(new MemoryStream(encoded, writable: false));
        var options2 = (writeOptions ?? new ProjectWriteOptions()) with { Preview = null, HostFingerprint = ProjectImages.Fingerprint(decoded.Pixels) };
        return ProjectEmbedding.Embed(encoded, format, ProjectWriter.WriteToBytes(project, options2));
    }

    /// <summary>Encodes a standalone .wpp file (with preview and thumbnail).</summary>
    public static byte[] EncodeProject(DocumentState state, PixelBuffer flat, ProjectWriteOptions? writeOptions = null) =>
        ProjectWriter.WriteToBytes(state, (writeOptions ?? new ProjectWriteOptions()) with { Preview = flat, HostFingerprint = null });

    /// <summary>Writes a file atomically (temp file in the same folder, then replace).</summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }
}
