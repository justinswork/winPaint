using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WinPaint.App.Controls;
using WinPaint.App.Resources;
using WinPaint.App.Services;
using WinPaint.Core.Document;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;
using WinPaint.Core.Text;
using WinPaint.Core.Tools;

namespace WinPaint.App.ViewModels;

/// <summary>Services the view model needs from the view (viewport geometry, clipboard).</summary>
public interface IViewService
{
    /// <summary>Visible canvas area (canvas px).</summary>
    PixelRect VisibleCanvasRect { get; }

    /// <summary>Fits the canvas to the window.</summary>
    void ZoomToFit();

    /// <summary>Closes the main window (after the unsaved prompt was handled).</summary>
    void CloseWindow();

    /// <summary>Enters or leaves full-screen view.</summary>
    void ShowFullScreen();

    /// <summary>Shows the thumbnail window.</summary>
    void ShowThumbnail(bool show);
}

/// <summary>
/// The main window's view model: owns the document, tools, settings and every command. Implements the tool host
/// and the canvas controller.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IToolHost, ICanvasController
{
    private readonly SettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<ToolKind, ITool> _tools = [];
    private int _seed;

    /// <summary>Creates the view model with a blank document.</summary>
    public MainViewModel(SettingsService settingsService, IDialogService dialogs, IClipboardService clipboard)
    {
        _settingsService = settingsService;
        _dialogs = dialogs;
        _clipboard = clipboard;
        Document = CreateBlankDocument();
        Text = new TextEditController(() => Document, CreateTextTemplate);
        Text.SessionChanged += OnTextSessionChanged;
        LoadSettings(settingsService.Current);
        AttachDocument();
        CreateTools();
        _activeTool = _tools[ActiveToolKind];
        Layers = new LayersViewModel(this);
        Layers.Refresh();
        UpdateTitle();
    }

    /// <inheritdoc/>
    public event EventHandler? DocumentReplaced;

    /// <inheritdoc/>
    public event EventHandler? OverlayChanged;

    /// <inheritdoc/>
    public event EventHandler<ZoomRequest>? ZoomRequested;

    /// <summary>View services (set by the window).</summary>
    public IViewService? View { get; set; }

    /// <inheritdoc/>
    public PaintDocument Document { get; private set; }

    /// <inheritdoc/>
    public ToolSettings Settings { get; } = new();

    /// <summary>Text editing state.</summary>
    public TextEditController Text { get; }

    /// <summary>Layers panel view model.</summary>
    public LayersViewModel Layers { get; }

    /// <inheritdoc/>
    public TextEditSession? TextSession => Text.Session;

    /// <inheritdoc/>
    public bool IsEditingText => Text.Session is not null;

    /// <summary>Window title.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Path of the open file (null for a new image).</summary>
    [ObservableProperty]
    public partial string? FilePath { get; set; }

    /// <summary>Transient status bar notice.</summary>
    [ObservableProperty]
    public partial string? StatusNotice { get; set; }

    /// <summary>Cursor position text.</summary>
    [ObservableProperty]
    public partial string CursorText { get; set; } = string.Empty;

    /// <summary>Selection size text.</summary>
    [ObservableProperty]
    public partial string SelectionText { get; set; } = string.Empty;

    /// <summary>Image size text.</summary>
    [ObservableProperty]
    public partial string ImageSizeText { get; set; } = string.Empty;

    /// <summary>File size on disk text.</summary>
    [ObservableProperty]
    public partial string FileSizeText { get; set; } = string.Empty;

    /// <inheritdoc/>
    public ToolOverlay? Overlay => (_activeTool as IOverlayTool)?.Overlay;

    /// <inheritdoc/>
    public int CursorSize => Settings.SizeFor(ActiveToolKind);

    /// <summary>Display name of the document.</summary>
    public string DocumentName => FilePath is null ? Strings.Untitled : Path.GetFileName(FilePath);

    // ---- IToolHost -------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public void ShowNotice(string? text) => StatusNotice = text;

    /// <inheritdoc/>
    public void SetColor(bool primary, Color color)
    {
        if (primary)
        {
            PrimaryColor = color;
        }
        else
        {
            SecondaryColor = color;
        }
    }

    /// <inheritdoc/>
    public void RestorePreviousTool() => ActiveToolKind = _previousTool;

    /// <inheritdoc/>
    public void ZoomStep(Point canvasPoint, bool zoomIn) => ZoomRequested?.Invoke(this, new ZoomRequest(canvasPoint, zoomIn));

    /// <inheritdoc/>
    public int NextSeed() => unchecked(++_seed * 7919);

    /// <inheritdoc/>
    public void ToolStateChanged()
    {
        UpdateSelectionText();
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public bool TryBeginTextEditAt(Point canvasPoint)
    {
        _textCaretRequest = canvasPoint;
        var ok = Text.TryBeginAt(canvasPoint);
        if (!ok)
        {
            _textCaretRequest = null;
        }

        return ok;
    }

    /// <inheritdoc/>
    public void BeginNewText(Rect box) => Text.BeginNew(box);

    /// <inheritdoc/>
    public void CommitTextEdit() => Text.Commit();

    // ---- ICanvasController -------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public ToolCursor CursorAt(Point canvasPoint) => _activeTool.CursorAt(canvasPoint);

    /// <inheritdoc/>
    public void PointerDown(PointerInput input)
    {
        StatusNotice = null;
        _activeTool.OnPointerDown(input);
        ToolStateChanged();
    }

    /// <inheritdoc/>
    public void PointerMove(PointerInput input)
    {
        _activeTool.OnPointerMove(input);
        if (input.Button != PointerButton.None)
        {
            ToolStateChanged();
        }
    }

    /// <inheritdoc/>
    public void PointerUp(PointerInput input)
    {
        _activeTool.OnPointerUp(input);
        ToolStateChanged();
    }

    /// <inheritdoc/>
    public void Tick() => _activeTool.OnTimer();

    /// <inheritdoc/>
    public void ReportCursor(Point? canvasPoint) =>
        CursorText = canvasPoint is { } p ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Status_Position, (int)Math.Floor(p.X), (int)Math.Floor(p.Y)) : string.Empty;

    /// <inheritdoc/>
    public void ReportResizePreview(Size? size)
    {
        if (size is { } s)
        {
            ImageSizeText = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Status_Size, (int)s.Width, (int)s.Height);
        }
        else
        {
            UpdateImageSizeText();
        }
    }

    /// <inheritdoc/>
    public void ResizeCanvas(int width, int height)
    {
        PrepareForCommand();
        ImageOperations.ResizeCanvas(Document, width, height, SecondaryColor);
        UpdateImageSizeText();
    }

    /// <inheritdoc/>
    public void EditorTextChanged(string text)
    {
        Text.Session?.Update(t => t.Text = text);
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void EditorBoxChanged(Rect box)
    {
        Text.Session?.Update(t => t.Box = box);
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void DeleteTextObject() => Text.Delete();

    /// <inheritdoc/>
    public bool EditorDoubleClick(Point canvasPoint)
    {
        var s = Text.Session;
        if (s is null || !s.IsNew || !s.IsBlank)
        {
            return false;
        }

        return TryBeginTextEditAt(canvasPoint);
    }

    // ---- Document management ------------------------------------------------------------------------------------

    /// <summary>Commits any open text session and pending tool operation before a document command.</summary>
    public void PrepareForCommand()
    {
        Text.Commit();
        _activeTool.CommitPending();
    }

    /// <summary>Replaces the document (New/Open).</summary>
    internal void ReplaceDocument(PaintDocument doc, string? path)
    {
        Text.Abandon();
        foreach (var tool in _tools.Values)
        {
            tool.Deactivate();
        }

        DetachDocument();
        Document = doc;
        FilePath = path;
        doc.History.BudgetBytes = Math.Max(64, _settingsService.Current.UndoBudgetMb) * 1024L * 1024L;
        AttachDocument();
        _activeTool.Activate();
        Layers.Refresh();
        UpdateTitle();
        UpdateImageSizeText();
        UpdateFileSizeText();
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
        ToolStateChanged();
    }

    private PaintDocument CreateBlankDocument()
    {
        var s = _settingsService.Current;
        var doc = new PaintDocument(Math.Clamp(s.NewWidth, 1, 100000), Math.Clamp(s.NewHeight, 1, 100000));
        doc.History.BudgetBytes = Math.Max(64, s.UndoBudgetMb) * 1024L * 1024L;
        return doc;
    }

    private void AttachDocument()
    {
        Document.History.Changed += OnHistoryChanged;
        Document.StructureChanged += OnStructureChanged;
        UpdateImageSizeText();
    }

    private void DetachDocument()
    {
        Document.History.Changed -= OnHistoryChanged;
        Document.StructureChanged -= OnStructureChanged;
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        UpdateTitle();
        Layers.Refresh();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnStructureChanged(object? sender, EventArgs e)
    {
        UpdateImageSizeText();
        Layers.Refresh();
    }

    private void UpdateTitle() =>
        Title = (Document.IsDirty ? Strings.DirtyPrefix : string.Empty) + string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.TitleFormat, DocumentName);

    private void UpdateImageSizeText() =>
        ImageSizeText = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Status_Size, Document.Width, Document.Height);

    private void UpdateFileSizeText()
    {
        if (FilePath is not null && File.Exists(FilePath))
        {
            var len = new FileInfo(FilePath).Length;
            FileSizeText = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Status_FileSize, FormatBytes(len));
        }
        else
        {
            FileSizeText = string.Empty;
        }
    }

    /// <summary>Formats a byte count like Explorer (KB/MB).</summary>
    internal static string FormatBytes(long len) => len switch
    {
        < 1024 => $"{len} B",
        < 1024 * 1024 => $"{len / 1024.0:0.#} KB",
        _ => $"{len / (1024.0 * 1024.0):0.##} MB",
    };

    private TextObject CreateTextTemplate() => new()
    {
        FontFamily = Settings.FontFamily,
        FontSizePt = Settings.FontSizePt,
        Bold = Settings.Bold,
        Italic = Settings.Italic,
        Underline = Settings.Underline,
        Strikethrough = Settings.Strikethrough,
        Foreground = Settings.Primary,
        Background = Settings.Secondary,
        OpaqueBackground = Settings.TextOpaque,
        Opacity = Settings.Opacity,
    };

    private void CreateTools()
    {
        _tools[ToolKind.Pencil] = new PencilTool(this);
        _tools[ToolKind.Brush] = new BrushTool(this);
        _tools[ToolKind.Eraser] = new EraserTool(this);
        _tools[ToolKind.Fill] = new FillTool(this);
        _tools[ToolKind.Picker] = new PickerTool(this);
        _tools[ToolKind.Magnifier] = new MagnifierTool(this);
        _tools[ToolKind.Text] = new TextTool(this);
        _tools[ToolKind.RectSelect] = new SelectionTool(this, freeForm: false);
        _tools[ToolKind.FreeSelect] = new SelectionTool(this, freeForm: true);
        _tools[ToolKind.Shape] = new ShapeTool(this);
    }
}
