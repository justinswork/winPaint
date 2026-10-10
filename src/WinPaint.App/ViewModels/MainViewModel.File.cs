using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPaint.App.Resources;
using WinPaint.App.Services;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;
using WinPaint.Core.Projects;

namespace WinPaint.App.ViewModels;

/// <summary>File menu: new, open, save, recent files, import, print, wallpaper, properties, exit.</summary>
public sealed partial class MainViewModel
{
    private bool _jpegWarned;
    private bool _notEditableWarned;
    private ProjectReadResult? _outsideProject;

    /// <summary>Recent files (most recent first, max 10).</summary>
    [ObservableProperty]
    public partial ObservableCollection<string> RecentFiles { get; set; } = [];

    /// <summary>True while a long operation runs (busy cursor).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// True when the image has an embedded winPaint version that wasn't restored because the picture was changed
    /// outside winPaint (WPP spec §6.5).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreProjectCommand))]
    public partial bool CanRestoreProject { get; set; }

    /// <summary>Format of the open file (null for a project or a new image).</summary>
    public ImageFormat? FileFormat { get; private set; }

    /// <summary>True when the open file is a winPaint project (.wpp).</summary>
    public bool IsProjectFile { get; private set; }

    /// <summary>Time the file was last saved (or its modification time when opened).</summary>
    public DateTime? LastSaved { get; private set; }

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        ReplaceDocument(CreateBlankDocument(), null);
        ResetFileState(null, null, project: false);
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
        OpenedFile opened;
        IsBusy = true;
        try
        {
            opened = await Task.Run(() => ProjectFiles.Open(path));
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
        var doc = opened.UseProject
            ? PaintDocument.FromState(opened.Project!.State)
            : PaintDocument.FromImage(opened.Image!.Pixels, opened.Image.DpiX, opened.Image.DpiY);
        ReplaceDocument(doc, full);
        var project = opened.Image is null;
        ResetFileState(project ? null : ImageFormats.FromPath(full), File.GetLastWriteTime(full), project);
        _outsideProject = opened.Status == EmbeddedProjectStatus.ChangedOutside ? opened.Project : null;
        CanRestoreProject = _outsideProject is not null;
        StatusNotice = opened.Status switch
        {
            EmbeddedProjectStatus.ChangedOutside => Strings.Status_ChangedOutside,
            EmbeddedProjectStatus.Ignored => string.Format(CultureInfo.CurrentCulture, Strings.Status_ProjectIgnored, opened.IgnoredReason),
            _ when opened.UseProject && opened.Project!.IsNewerMinorVersion => Strings.Status_NewerVersion,
            _ when opened.UseProject && opened.Project!.DamagedParts > 0 => Strings.Status_DamagedParts,
            _ => null,
        };
        AddRecent(full);
        return true;
    }

    /// <summary>
    /// Opens the winPaint version embedded in an image that was changed outside winPaint, as a new unsaved document.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestoreProject))]
    private async Task RestoreProjectAsync()
    {
        var project = _outsideProject;
        if (project is null || !await ConfirmDiscardAsync())
        {
            return;
        }

        var doc = PaintDocument.FromState(project.State);
        doc.History.Reset(doc.CaptureState(), markSaved: false);
        ReplaceDocument(doc, null);
        ResetFileState(null, null, project: false);
        UpdateTitle();
    }

    private void ResetFileState(ImageFormat? format, DateTime? lastSaved, bool project)
    {
        FileFormat = format;
        IsProjectFile = project;
        LastSaved = lastSaved;
        _jpegWarned = format == ImageFormat.Jpeg;
        _notEditableWarned = false;
        _outsideProject = null;
        CanRestoreProject = false;
        StatusNotice = null;
    }

    [RelayCommand]
    private async Task SaveAsync() => await SaveCoreAsync(saveAs: false);

    [RelayCommand]
    private async Task SaveAsAsync() => await SaveCoreAsync(saveAs: true);

    [RelayCommand]
    private async Task SaveAsPlainAsync() => await SaveCoreAsync(saveAs: true, plain: true);

    /// <summary>Saves (or Save As when untitled). Returns false when cancelled or failed.</summary>
    public async Task<bool> SaveCoreAsync(bool saveAs, bool plain = false)
    {
        PrepareForCommand();
        SaveTarget target;
        if (saveAs || FilePath is null)
        {
            var name = FilePath is null ? Strings.Untitled + ".png" : Path.GetFileName(FilePath);
            var pick = _dialogs.PickSaveFile(name, FileFormat ?? ImageFormat.Png, project: IsProjectFile && !plain, allowProject: !plain);
            if (pick is null)
            {
                return false;
            }

            target = pick;
        }
        else
        {
            target = new SaveTarget(FilePath, FileFormat ?? ImageFormats.FromPath(FilePath) ?? ImageFormat.Png, IsProjectFile);
        }

        return await SaveToAsync(target, plain);
    }

    /// <summary>
    /// Writes the document. Images get the project embedded when it has something worth keeping and the format
    /// allows it (WPP spec §7.1); <paramref name="plain"/> writes a plain image regardless.
    /// </summary>
    public async Task<bool> SaveToAsync(SaveTarget target, bool plain)
    {
        ArgumentNullException.ThrowIfNull(target);
        var (path, format) = (target.Path, target.Format);
        var state = Document.CaptureState();
        var flat = Document.Flatten();
        var options = new EncodeOptions { DpiX = Document.DpiX, DpiY = Document.DpiY };
        var worth = !plain && _settingsService.Current.KeepTextEditable && ProjectFormat.IsWorthKeeping(state);
        var embed = !target.IsProject && worth && ProjectEmbedding.CanEmbed(format);
        if (!target.IsProject)
        {
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

            if (worth && !embed && !_notEditableWarned)
            {
                if (!_dialogs.Confirm(Strings.Msg_NotEditableFormat))
                {
                    return false;
                }

                _notEditableWarned = true;
            }
        }

        var writeOptions = new ProjectWriteOptions { GeneratorVersion = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" };
        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                var bytes = target.IsProject
                    ? ProjectFiles.EncodeProject(state, flat, writeOptions)
                    : ProjectFiles.EncodeImage(flat, format, options, embed ? state : null, writeOptions);
                ProjectFiles.WriteAtomic(path, bytes);
            });
        }
        catch (ProjectFormatException)
        {
            _dialogs.ShowError(Strings.Msg_ProjectTooLarge);
            return false;
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

        if (format == ImageFormat.Jpeg && !target.IsProject)
        {
            _jpegWarned = true;
        }

        FilePath = Path.GetFullPath(path);
        FileFormat = target.IsProject ? null : format;
        IsProjectFile = target.IsProject;
        LastSaved = DateTime.Now;
        _outsideProject = null;
        CanRestoreProject = false;
        var hidden = ProjectFormat.HiddenLayerCount(state);
        StatusNotice = target.IsProject ? Strings.Status_ProjectSaved
            : !embed ? null
            : hidden > 0 ? string.Format(CultureInfo.CurrentCulture, Strings.Status_ProjectEmbeddedHidden, hidden)
            : Strings.Status_ProjectEmbedded;
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

        var file = FilePath!;
        if (IsProjectFile)
        {
            // Windows can't show a .wpp: hand it a flattened copy.
            file = Path.Combine(Path.GetDirectoryName(_settingsService.FilePath) ?? Path.GetTempPath(), "wallpaper.png");
            ImageCodec.Encode(Document.Flatten(), file, ImageFormat.Png);
        }

        if (!Wallpaper.Set(file, style))
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
        var flat = Document.Flatten();
        try
        {
            var state = Document.CaptureState();
            ProjectFiles.WriteAtomic(path, ProjectFiles.EncodeImage(flat, ImageFormat.Png, null, ProjectFormat.IsWorthKeeping(state) ? state : null));
        }
        catch (ProjectFormatException)
        {
            ImageCodec.Encode(flat, path, ImageFormat.Png);
        }

        return path;
    }

    /// <summary>A snapshot of the composite for diagnostics.</summary>
    internal PixelBuffer Composite() => Document.Flatten(includeFloating: true);
}
