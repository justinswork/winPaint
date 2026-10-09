using System.Windows;
using System.Windows.Media;
using WinPaint.Core.Document;

namespace WinPaint.Core.Text;

/// <summary>How an edit session ended.</summary>
public enum TextEditOutcome
{
    /// <summary>Nothing changed; no undo step.</summary>
    NoChange,

    /// <summary>A new text object was created (one undo step).</summary>
    Created,

    /// <summary>An existing object changed (one undo step).</summary>
    Edited,

    /// <summary>An existing object was deleted (one undo step).</summary>
    Deleted,

    /// <summary>A new, empty object was discarded (no undo step).</summary>
    Discarded,
}

/// <summary>
/// One editing session of a live text object. The object is edited in place in the document (so the real
/// renderer draws it live) and the whole session becomes a single undo step when it ends.
/// </summary>
public sealed class TextEditSession
{
    private readonly PaintDocument _doc;
    private readonly TextObject? _before;

    private TextEditSession(PaintDocument doc, Layer layer, TextObject text, bool isNew)
    {
        _doc = doc;
        Layer = layer;
        Text = text;
        IsNew = isNew;
        _before = isNew ? null : text.CloneText();
    }

    /// <summary>The live object being edited.</summary>
    public TextObject Text { get; }

    /// <summary>The layer holding the object.</summary>
    public Layer Layer { get; }

    /// <summary>True when the object was created by this session.</summary>
    public bool IsNew { get; }

    /// <summary>True once the session has ended.</summary>
    public bool IsEnded { get; private set; }

    /// <summary>True when the text has no visible characters.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Text.Text);

    /// <summary>
    /// Starts a session for a new text object on the active layer. The object is inserted immediately (empty).
    /// </summary>
    public static TextEditSession BeginNew(PaintDocument doc, Rect box, TextObject template)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(template);
        var t = template.CloneWithNewId();
        t.Text = string.Empty;
        t.Box = box;
        t.Transform = Matrix.Identity;
        var layer = doc.ActiveLayer;
        TextOperations.Insert(doc, layer, t);
        return new TextEditSession(doc, layer, t, isNew: true);
    }

    /// <summary>Starts a session on an existing live text object (making its layer active).</summary>
    public static TextEditSession BeginExisting(PaintDocument doc, TextHit hit)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(hit);
        doc.ActiveLayerIndex = doc.Layers.IndexOf(hit.Layer);
        return new TextEditSession(doc, hit.Layer, hit.Text, isNew: false);
    }

    /// <summary>Applies a change (content, box, formatting) and repaints.</summary>
    public void Update(Action<TextObject> change)
    {
        ThrowIfEnded();
        TextOperations.Update(_doc, Layer, Text, change);
    }

    /// <summary>
    /// Ends the session. Blank text discards a new object (no undo step) or deletes an existing one (one step).
    /// </summary>
    public TextEditOutcome Commit()
    {
        ThrowIfEnded();
        IsEnded = true;
        if (IsBlank)
        {
            return RemoveObject();
        }

        // Persist the auto-grown height so the box no longer shrinks when text is later removed.
        var effective = TextLayoutEngine.EffectiveBox(Text);
        if (effective.Height > Text.Box.Height)
        {
            Text.Box = new Rect(Text.Box.X, Text.Box.Y, Text.Box.Width, effective.Height);
        }

        if (IsNew)
        {
            _doc.Commit("Text");
            return TextEditOutcome.Created;
        }

        if (_before is not null && _before.StateEquals(Text))
        {
            return TextEditOutcome.NoChange;
        }

        _doc.Commit("Edit text");
        return TextEditOutcome.Edited;
    }

    /// <summary>Removes the object entirely (Delete text). Ends the session.</summary>
    public TextEditOutcome Delete()
    {
        ThrowIfEnded();
        IsEnded = true;
        return RemoveObject();
    }

    private TextEditOutcome RemoveObject()
    {
        if (IsNew)
        {
            // Restore the exact committed state: no stray segment, no undo entry.
            _doc.RevertUncommitted();
            return TextEditOutcome.Discarded;
        }

        TextOperations.Remove(_doc, Layer, Text);
        _doc.Commit("Delete text");
        return TextEditOutcome.Deleted;
    }

    private void ThrowIfEnded()
    {
        if (IsEnded)
        {
            throw new InvalidOperationException("The text edit session has ended.");
        }
    }
}
