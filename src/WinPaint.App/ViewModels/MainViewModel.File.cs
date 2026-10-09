using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPaint.App.Resources;
using WinPaint.App.Services;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.App.ViewModels;

/// <summary>File menu: new, open, save, recent files, import, print, wallpaper, properties, exit.</summary>
public sealed partial class MainViewModel
{
    private bool _jpegWarned;

    /// <summary>Recent files (most recent first, max 10).</summary>
    [ObservableProperty]
    public partial ObservableCollection<string> RecentFiles { get; set; } = [];

    /// <summary>True while a long operation runs (busy cursor).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Format of the open file.</summary>
    public ImageFormat? FileFormat { get; private set; }

    /// <summary>Time the file was last saved (or its modification time when opened).</summary>
    public DateTime? LastSaved { get; private set; }

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        FileFormat = null;
        LastSaved = null;
        _jpegWarned = false;
        ReplaceDocument(CreateBlankDocument(), null);
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        var path = _dialogs.PickOpenFile();
        if (path is not null)
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (path is null)
        {
            return;
        }

        if (!File.Exists(path))
        {
            _dialogs.Info(string.Format(CultureInfo.CurrentCulture, Strings.Msg_RecentMissing, path));
            RecentFiles.Remove(path);
            SaveSettings();
            return;
        }

        if (await ConfirmDiscardAsync())
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand]
    private void ClearRecent()
    {
        RecentFiles.Clear();
        SaveSettings();
    }

    /// <summary>Opens a dropped file (with the unsaved-changes prompt).</summary>
    public async Task OpenDroppedAsync(string path)
    {
        if (await ConfirmDiscardAsync())
        {
            await OpenPathAsync(path);
        }
    }

    /// <summary>Opens a file without prompting (command line / after the prompt). Returns false on error.</summary>
    public async Task<bool> OpenPathAsync(string path)
    {
        DecodedImage decoded;
        IsBusy = true;
        try
        {
            decoded = await Task.Run(() => ImageCodec.Decode(path));
        }
        catch (ImageOpenException ex)
        {
            _dialogs.ShowError(string.Format(CultureInfo.CurrentCulture, Strings.Msg_OpenFailed, ex.Message));
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        var full = Path.GetFullPath(path);
        ReplaceDocument(PaintDocument.FromImage(decoded.Pixels, decoded.DpiX, decoded.DpiY), full);
        FileFormat = ImageFormats.FromPath(full);
        LastSaved = File.GetLastWriteTime(full);
        _jpegWarned = FileFormat == ImageFormat.Jpeg;
        AddRecent(full);
        return true;
    }

    [RelayCommand]
    private async Task SaveAsync() => await SaveCoreAsync(saveAs: false);

    [RelayCommand]
    private async Task SaveAsAsync() => await SaveCoreAsync(saveAs: true);

    /// <summary>Saves (or Save As when untitled). Returns false when cancelled or failed.</summary>
    public async Task<bool> SaveCoreAsync(bool saveAs)
    {
        PrepareForCommand();
        string path;
        ImageFormat format;
        if (saveAs || FilePath is null)
        {
            var name = FilePath is null ? Strings.Untitled + ".png" : Path.GetFileName(FilePath);
            var pick = _dialogs.PickSaveFile(name, FileFormat ?? ImageFormat.Png);
            if (pick is null)
            {
                return false;
            }

            (path, format) = pick.Value;
        }
        else
        {
            path = FilePath;
            format = FileFormat ?? ImageFormats.FromPath(path) ?? ImageFormat.Png;
        }

        return await SaveToAsync(path, format);
    }

    /// <summary>Writes the flattened image to <paramref name="path"/> (the in-memory document is unchanged).</summary>
    public async Task<bool> SaveToAsync(string path, ImageFormat format)
    {
        var flat = Document.Flatten();
        var options = new EncodeOptions { DpiX = Document.DpiX, DpiY = Document.DpiY };
        switch (format)
        {
            case ImageFormat.Jpeg when flat.HasTransparency() && !_jpegWarned:
                if (!_dialogs.Confirm(Strings.Msg_JpegTransparency))
                {
                    return false;
                }

                break;
            case ImageFormat.Gif:
                if (!_dialogs.Confirm(Strings.Msg_GifQuality))
                {
                    return false;
                }

                break;
            case ImageFormat.Ico:
                var sizes = _dialogs.PickIcoSizes();
                if (sizes is null || (flat.Width != flat.Height && !_dialogs.Confirm(Strings.Msg_IcoNotSquare)))
                {
                    return false;
                }

                options = options with { IcoSizes = sizes };
                break;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => ImageCodec.Encode(flat, path, format, options));
        }
        catch (IOException ex)
        {
            _dialogs.ShowError(string.Format(CultureInfo.CurrentCulture, Strings.Msg_SaveFailed, ex.Message));
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _dialogs.ShowError(string.Format(CultureInfo.CurrentCulture, Strings.Msg_SaveFailed, ex.Message));
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        if (format == ImageFormat.Jpeg)
        {
            _jpegWarned = true;
        }

        FilePath = Path.GetFullPath(path);
        FileFormat = format;
        LastSaved = DateTime.Now;
        Document.History.MarkSaved();
        UpdateTitle();
        UpdateFileSizeText();
        AddRecent(FilePath);
        return true;
    }

    /// <summary>Unsaved-changes prompt. Returns true when it is OK to discard the current document.</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        Text.Commit();
        _activeTool.CommitPending();
        if (!Document.IsDirty)
        {
            return true;
        }

        return _dialogs.AskSaveChanges(DocumentName) switch
        {
            SaveChoice.Save => await SaveCoreAsync(saveAs: false),
            SaveChoice.DontSave => true,
            _ => false,
        };
    }

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            SaveSettings();
            View?.CloseWindow();
        }
    }

    private bool _closeConfirmed;

    /// <summary>Called by the window when it is closing; returns true when it may close.</summary>
    public async Task<bool> CanCloseAsync()
    {
        if (_closeConfirmed)
        {
            return true;
        }

        var ok = await ConfirmDiscardAsync();
        if (ok)
        {
            _closeConfirmed = true;
            SaveSettings();
        }

        return ok;
    }

    [RelayCommand]
    private void Print()
    {
        PrepareForCommand();
        _dialogs.Print(Document.Flatten(), Document.DpiX, Document.DpiY, DocumentName);
    }

    [RelayCommand]
    private void PageSetup() => _dialogs.PageSetup();

    [RelayCommand]
    private void PrintPreview()
    {
        PrepareForCommand();
        _dialogs.PrintPreview(Document.Flatten(), Document.DpiX, Document.DpiY, DocumentName);
    }

    [RelayCommand]
    private async Task SetDesktopAsync(WallpaperStyle style)
    {
        PrepareForCommand();
        if (FilePath is null || Document.IsDirty)
        {
            if (!_dialogs.Confirm(Strings.Msg_MustSaveForWallpaper) || !await SaveCoreAsync(saveAs: false))
            {
                return;
            }
        }

        if (!Wallpaper.Set(FilePath!, style))
        {
            _dialogs.ShowError(Strings.Msg_WallpaperFailed);
        }
    }

    [RelayCommand]
    private async Task ImagePropertiesAsync()
    {
        PrepareForCommand();
        long? size = FilePath is not null && File.Exists(FilePath) ? new FileInfo(FilePath).Length : null;
        var r = _dialogs.ImageProperties(new ImagePropertiesInfo(Document.Width, Document.Height, Document.DpiX, Document.DpiY, FilePath is null ? null : LastSaved, size));
        if (r is null)
        {
            return;
        }

        if (r.Width != Document.Width || r.Height != Document.Height)
        {
            ImageOperations.ResizeCanvas(Document, r.Width, r.Height, SecondaryColor);
        }

        if (r.BlackAndWhite)
        {
            await RunImageOperationAsync(o => ImageOperations.BlackAndWhiteAsync(Document, o));
        }

        UpdateImageSizeText();
    }

    private void AddRecent(string path)
    {
        var existing = RecentFiles.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RecentFiles.Remove(existing);
        }

        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 10)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }

        SaveSettings();
    }

    /// <summary>Flattened PNG recovery copy (used by the crash handler).</summary>
    public string? SaveRecoveryCopy()
    {
        var dir = Environment.GetEnvironmentVariable("WINPAINT_RECOVERY_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "winPaint Recovery");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"recovery-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        ImageCodec.Encode(Document.Flatten(), path, ImageFormat.Png);
        return path;
    }

    /// <summary>A snapshot of the composite for diagnostics.</summary>
    internal PixelBuffer Composite() => Document.Flatten(includeFloating: true);
}
