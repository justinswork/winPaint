using System.Windows;
using WinPaint.Core.Document;

namespace WinPaint.Core.Text;

/// <summary>
/// Owns the (at most one) open text edit session for a document and implements the open/commit/delete rules
/// shared by the Text tool, the Select tool and the editor overlay.
/// </summary>
public sealed class TextEditController(Func<PaintDocument> document, Func<TextObject> template)
{
    /// <summary>Raised when a session starts or ends.</summary>
    public event EventHandler? SessionChanged;

    /// <summary>The open session.</summary>
    public TextEditSession? Session { get; private set; }

    /// <summary>Outcome of the last ended session.</summary>
    public TextEditOutcome? LastOutcome { get; private set; }

    /// <summary>
    /// Opens the topmost live text at <paramref name="p"/> (ignoring the object currently being edited).
    /// A blank new box from the first click of a double-click is discarded first.
    /// </summary>
    public bool TryBeginAt(Point p)
    {
        var doc = document();
        var hit = HitTestExcluding(doc, p, Session?.Text);
        if (hit is null)
        {
            return false;
        }

        if (Session is not null)
        {
            Commit();

            // Committing may restore the document state (discarding a blank box), which recreates objects.
            hit = HitTestExcluding(doc, p, null);
            if (hit is null)
            {
                return false;
            }
        }

        Session = TextEditSession.BeginExisting(doc, hit);
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Starts a new text object (commits any open session first).</summary>
    public void BeginNew(Rect box)
    {
        if (Session is not null)
        {
            Commit();
        }

        Session = TextEditSession.BeginNew(document(), box, template());
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Commits the open session, if any.</summary>
    public TextEditOutcome? Commit()
    {
        if (Session is null)
        {
            return null;
        }

        var s = Session;
        Session = null;
        LastOutcome = s.Commit();
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return LastOutcome;
    }

    /// <summary>Deletes the object being edited.</summary>
    public TextEditOutcome? Delete()
    {
        if (Session is null)
        {
            return null;
        }

        var s = Session;
        Session = null;
        LastOutcome = s.Delete();
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return LastOutcome;
    }

    /// <summary>Drops the session without committing (used when the document is replaced).</summary>
    public void Abandon()
    {
        if (Session is not null)
        {
            Session = null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static TextHit? HitTestExcluding(PaintDocument doc, Point p, TextObject? exclude)
    {
        for (var li = doc.Layers.Count - 1; li >= 0; li--)
        {
            var layer = doc.Layers[li];
            if (!layer.Visible)
            {
                continue;
            }

            for (var ei = layer.Elements.Count - 1; ei >= 0; ei--)
            {
                if (layer.Elements[ei] is TextObject t && !ReferenceEquals(t, exclude) && TextLayoutEngine.HitTest(t, p))
                {
                    return new TextHit(layer, t);
                }
            }
        }

        return null;
    }
}
