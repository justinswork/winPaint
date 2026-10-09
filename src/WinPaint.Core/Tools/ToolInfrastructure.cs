using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinPaint.Core.Brushes;
using WinPaint.Core.Document;
using WinPaint.Core.Shapes;

namespace WinPaint.Core.Tools;

/// <summary>Every tool on the toolbar.</summary>
public enum ToolKind
{
    /// <summary>Rectangular selection.</summary>
    RectSelect,

    /// <summary>Free-form (lasso) selection.</summary>
    FreeSelect,

    /// <summary>Pencil.</summary>
    Pencil,

    /// <summary>Fill bucket.</summary>
    Fill,

    /// <summary>Text.</summary>
    Text,

    /// <summary>Eraser.</summary>
    Eraser,

    /// <summary>Color picker.</summary>
    Picker,

    /// <summary>Magnifier.</summary>
    Magnifier,

    /// <summary>Brushes (kind from settings).</summary>
    Brush,

    /// <summary>Shapes (kind from settings).</summary>
    Shape,
}

/// <summary>Mouse buttons relevant to tools.</summary>
public enum PointerButton
{
    /// <summary>Left button.</summary>
    Left,

    /// <summary>Right button.</summary>
    Right,

    /// <summary>Middle button.</summary>
    Middle,

    /// <summary>No button (hover).</summary>
    None,
}

/// <summary>Cursor a tool wants over the canvas.</summary>
public enum ToolCursor
{
    /// <summary>Standard arrow.</summary>
    Arrow,

    /// <summary>Crosshair (shapes, selection).</summary>
    Crosshair,

    /// <summary>Brush-size circle.</summary>
    BrushCircle,

    /// <summary>Eraser square.</summary>
    EraserSquare,

    /// <summary>Pencil.</summary>
    Pencil,

    /// <summary>I-beam (text).</summary>
    IBeam,

    /// <summary>Paint bucket.</summary>
    Bucket,

    /// <summary>Dropper.</summary>
    Dropper,

    /// <summary>Magnifier.</summary>
    Magnifier,

    /// <summary>Move (four arrows).</summary>
    Move,

    /// <summary>Resize NW-SE.</summary>
    SizeNwse,

    /// <summary>Resize NE-SW.</summary>
    SizeNesw,

    /// <summary>Resize horizontal.</summary>
    SizeWe,

    /// <summary>Resize vertical.</summary>
    SizeNs,
}

/// <summary>A pointer event in canvas coordinates.</summary>
/// <param name="Position">Canvas position (fractional pixels).</param>
/// <param name="Button">Button pressed (for down/up) or held (for move).</param>
/// <param name="Modifiers">Keyboard modifiers.</param>
/// <param name="ClickCount">1 for single, 2 for double click.</param>
public readonly record struct PointerInput(Point Position, PointerButton Button, ModifierKeys Modifiers, int ClickCount = 1)
{
    /// <summary>Shift held.</summary>
    public bool Shift => (Modifiers & ModifierKeys.Shift) != 0;

    /// <summary>Ctrl held.</summary>
    public bool Ctrl => (Modifiers & ModifierKeys.Control) != 0;

    /// <summary>Integer pixel under the pointer.</summary>
    public (int X, int Y) Pixel => ((int)Math.Floor(Position.X), (int)Math.Floor(Position.Y));
}

/// <summary>Shared drawing settings (colors, size, brush/shape choices).</summary>
public sealed class ToolSettings
{
    /// <summary>Color 1.</summary>
    public Color Primary { get; set; } = Colors.Black;

    /// <summary>Color 2.</summary>
    public Color Secondary { get; set; } = Colors.White;

    /// <summary>Pencil size (px).</summary>
    public int PencilSize { get; set; } = 1;

    /// <summary>Brush size (px).</summary>
    public int BrushSize { get; set; } = 8;

    /// <summary>Eraser size (px).</summary>
    public int EraserSize { get; set; } = 8;

    /// <summary>Shape outline width (px).</summary>
    public int ShapeSize { get; set; } = 2;

    /// <summary>Opacity 0.01..1 for brushes, pencil and shapes.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Current brush.</summary>
    public BrushKind Brush { get; set; }

    /// <summary>Current shape.</summary>
    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;

    /// <summary>Shape outline style.</summary>
    public ShapeStyle Outline { get; set; } = ShapeStyle.Solid;

    /// <summary>Shape fill style.</summary>
    public ShapeStyle Fill { get; set; } = ShapeStyle.None;

    /// <summary>Transparent selection toggle.</summary>
    public bool TransparentSelection { get; set; }

    /// <summary>Text defaults: font family.</summary>
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>Text defaults: font size (pt).</summary>
    public double FontSizePt { get; set; } = 11;

    /// <summary>Text defaults: bold.</summary>
    public bool Bold { get; set; }

    /// <summary>Text defaults: italic.</summary>
    public bool Italic { get; set; }

    /// <summary>Text defaults: underline.</summary>
    public bool Underline { get; set; }

    /// <summary>Text defaults: strikethrough.</summary>
    public bool Strikethrough { get; set; }

    /// <summary>Text defaults: opaque background.</summary>
    public bool TextOpaque { get; set; }

    /// <summary>Size used by a tool.</summary>
    public int SizeFor(ToolKind tool) => tool switch
    {
        ToolKind.Pencil => PencilSize,
        ToolKind.Eraser => EraserSize,
        ToolKind.Shape => ShapeSize,
        _ => BrushSize,
    };

    /// <summary>Sets the size used by a tool (clamped to 1..100).</summary>
    public void SetSizeFor(ToolKind tool, int size)
    {
        size = Math.Clamp(size, 1, 100);
        switch (tool)
        {
            case ToolKind.Pencil:
                PencilSize = size;
                break;
            case ToolKind.Eraser:
                EraserSize = size;
                break;
            case ToolKind.Shape:
                ShapeSize = size;
                break;
            default:
                BrushSize = size;
                break;
        }
    }

    /// <summary>True for tools whose size is adjustable (Ctrl+Plus/Minus changes size instead of zoom).</summary>
    public static bool HasSize(ToolKind tool) => tool is ToolKind.Pencil or ToolKind.Eraser or ToolKind.Brush or ToolKind.Shape;
}

/// <summary>Services the UI provides to tools.</summary>
public interface IToolHost
{
    /// <summary>The document being edited.</summary>
    PaintDocument Document { get; }

    /// <summary>Drawing settings.</summary>
    ToolSettings Settings { get; }

    /// <summary>Shows a transient status-bar notice (null clears).</summary>
    void ShowNotice(string? text);

    /// <summary>Sets color 1 or 2 (primary when <paramref name="primary"/>).</summary>
    void SetColor(bool primary, Color color);

    /// <summary>Switches back to the tool used before the current one.</summary>
    void RestorePreviousTool();

    /// <summary>Zooms one step in or out keeping <paramref name="canvasPoint"/> under the cursor.</summary>
    void ZoomStep(Point canvasPoint, bool zoomIn);

    /// <summary>Seed for brush randomness (deterministic per stroke index).</summary>
    int NextSeed();

    /// <summary>Notifies that tool-specific UI state (handles, overlays) changed.</summary>
    void ToolStateChanged();

    /// <summary>True while a text object is open in the editor.</summary>
    bool IsEditingText { get; }

    /// <summary>
    /// Opens the topmost live text object at a canvas point for editing (caret at the point). Returns false when
    /// there is no text there.
    /// </summary>
    bool TryBeginTextEditAt(Point canvasPoint);

    /// <summary>Starts editing a new text object with the given box on the active layer.</summary>
    void BeginNewText(Rect box);

    /// <summary>Ends the open text edit session (commit; blank text is discarded/deleted).</summary>
    void CommitTextEdit();

    /// <summary>Half-size of resize handles in canvas pixels at the current zoom.</summary>
    double HandleTolerance { get; }

    /// <summary>Reports that live text was flattened by a selection edit (status-bar notice).</summary>
    void TextFlattened();

    /// <summary>Requests the selection context menu at the pointer.</summary>
    void ShowContextMenu();
}

/// <summary>A canvas tool. Tools mutate the document and commit history steps.</summary>
public interface ITool
{
    /// <summary>Which tool this is.</summary>
    ToolKind Kind { get; }

    /// <summary>Cursor for a canvas point.</summary>
    ToolCursor CursorAt(Point canvasPoint);

    /// <summary>Called when the tool becomes active.</summary>
    void Activate();

    /// <summary>Called before switching away: commit or cancel any floating operation.</summary>
    void Deactivate();

    /// <summary>Pointer pressed.</summary>
    void OnPointerDown(PointerInput input);

    /// <summary>Pointer moved (button may be <see cref="PointerButton.None"/>).</summary>
    void OnPointerMove(PointerInput input);

    /// <summary>Pointer released.</summary>
    void OnPointerUp(PointerInput input);

    /// <summary>Key pressed while the canvas has focus. Returns true when handled.</summary>
    bool OnKey(Key key, ModifierKeys modifiers);

    /// <summary>Periodic tick while a pointer button is held (airbrush).</summary>
    void OnTimer();

    /// <summary>True when a floating operation is in progress (Esc/Enter commit it).</summary>
    bool HasPendingOperation { get; }

    /// <summary>Commits any pending floating operation.</summary>
    void CommitPending();
}
