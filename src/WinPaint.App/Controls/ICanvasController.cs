using System.Windows;
using WinPaint.Core.Document;
using WinPaint.Core.Text;
using WinPaint.Core.Tools;

namespace WinPaint.App.Controls;

/// <summary>What the canvas control needs from the view model (input routing, overlays, text editing).</summary>
public interface ICanvasController
{
    /// <summary>Raised when <see cref="Document"/> is replaced (new/open).</summary>
    event EventHandler? DocumentReplaced;

    /// <summary>Raised when tool overlays or the text session changed and the overlay must be redrawn.</summary>
    event EventHandler? OverlayChanged;

    /// <summary>Raised when the view model wants a zoom step at a canvas point (magnifier).</summary>
    event EventHandler<ZoomRequest>? ZoomRequested;

    /// <summary>The document.</summary>
    PaintDocument Document { get; }

    /// <summary>The active tool kind.</summary>
    ToolKind ActiveToolKind { get; }

    /// <summary>Overlay visuals of the active tool.</summary>
    ToolOverlay? Overlay { get; }

    /// <summary>Size (px) of the brush cursor preview for the active tool.</summary>
    int CursorSize { get; }

    /// <summary>The open text editing session, if any.</summary>
    TextEditSession? TextSession { get; }

    /// <summary>Cursor for a canvas point.</summary>
    ToolCursor CursorAt(Point canvasPoint);

    /// <summary>Pointer pressed on the canvas.</summary>
    void PointerDown(PointerInput input);

    /// <summary>Pointer moved.</summary>
    void PointerMove(PointerInput input);

    /// <summary>Pointer released.</summary>
    void PointerUp(PointerInput input);

    /// <summary>Periodic tick while a button is held.</summary>
    void Tick();

    /// <summary>Reports the cursor position for the status bar (null when outside the canvas).</summary>
    void ReportCursor(Point? canvasPoint);

    /// <summary>Reports the live canvas-resize preview size (null when finished).</summary>
    void ReportResizePreview(Size? size);

    /// <summary>Resizes the canvas (drag handles), anchored top-left.</summary>
    void ResizeCanvas(int width, int height);

    /// <summary>The text editor's content changed.</summary>
    void EditorTextChanged(string text);

    /// <summary>The text box was moved or resized by dragging its border/HandleBox.</summary>
    void EditorBoxChanged(Rect box);

    /// <summary>Ends the text session (click outside, Esc).</summary>
    void CommitTextEdit();

    /// <summary>Deletes the text object being edited.</summary>
    void DeleteTextObject();

    /// <summary>A double-click landed inside the editor at a canvas point.</summary>
    bool EditorDoubleClick(Point canvasPoint);
}

/// <summary>A zoom step request.</summary>
/// <param name="CanvasPoint">Point to keep under the cursor.</param>
/// <param name="ZoomIn">True to zoom in.</param>
public sealed record ZoomRequest(Point CanvasPoint, bool ZoomIn);
