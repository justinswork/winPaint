namespace WinPaint.Core.History;

/// <summary>A committed undo step.</summary>
/// <param name="Name">Human readable description.</param>
/// <param name="State">The document state after the step.</param>
/// <param name="Bytes">Estimated memory held exclusively by this step.</param>
public sealed record HistoryEntry(string Name, DocumentState State, long Bytes);

/// <summary>
/// Linear undo/redo history of document states with a memory budget. The oldest steps are evicted first once the
/// budget is exceeded (the current state is never evicted).
/// </summary>
public sealed class HistoryManager
{
    /// <summary>Default memory budget: 1 GB.</summary>
    public const long DefaultBudgetBytes = 1L << 30;

    private const long TextObjectBytes = 1024;
    private readonly List<HistoryEntry> _entries = [];
    private int _index = -1;
    private DocumentState? _saved;

    /// <summary>Raised whenever the history changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Memory budget in bytes.</summary>
    public long BudgetBytes { get; set; } = DefaultBudgetBytes;

    /// <summary>Total estimated bytes held by history steps beyond the baseline.</summary>
    public long TotalBytes => _entries.Skip(1).Sum(e => e.Bytes);

    /// <summary>Number of entries including the baseline.</summary>
    public int Count => _entries.Count;

    /// <summary>Number of steps that can be undone.</summary>
    public int UndoCount => Math.Max(0, _index);

    /// <summary>Number of steps that can be redone.</summary>
    public int RedoCount => _entries.Count - 1 - _index;

    /// <summary>True when undo is possible.</summary>
    public bool CanUndo => _index > 0;

    /// <summary>True when redo is possible.</summary>
    public bool CanRedo => _index < _entries.Count - 1;

    /// <summary>The state matching the live document.</summary>
    public DocumentState Current => _entries[_index].State;

    /// <summary>Name of the step that would be undone.</summary>
    public string? UndoName => CanUndo ? _entries[_index].Name : null;

    /// <summary>Name of the step that would be redone.</summary>
    public string? RedoName => CanRedo ? _entries[_index + 1].Name : null;

    /// <summary>True when the current state differs from the last saved one.</summary>
    public bool IsDirty => _index < 0 || !ReferenceEquals(_entries[_index].State, _saved);

    /// <summary>Clears history and sets the baseline state.</summary>
    public void Reset(DocumentState baseline, bool markSaved)
    {
        _entries.Clear();
        _entries.Add(new HistoryEntry("Open", baseline, 0));
        _index = 0;
        _saved = markSaved ? baseline : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a step after the current one (drops any redo steps).</summary>
    public void Commit(string name, DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_index < _entries.Count - 1)
        {
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        }

        _entries.Add(new HistoryEntry(name, state, EstimateBytes(_index >= 0 ? _entries[_index].State : null, state)));
        _index = _entries.Count - 1;
        Evict();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves one step back and returns the state to restore.</summary>
    public DocumentState Undo()
    {
        if (!CanUndo)
        {
            throw new InvalidOperationException("Nothing to undo.");
        }

        _index--;
        Changed?.Invoke(this, EventArgs.Empty);
        return _entries[_index].State;
    }

    /// <summary>Moves one step forward and returns the state to restore.</summary>
    public DocumentState Redo()
    {
        if (!CanRedo)
        {
            throw new InvalidOperationException("Nothing to redo.");
        }

        _index++;
        Changed?.Invoke(this, EventArgs.Empty);
        return _entries[_index].State;
    }

    /// <summary>Marks the current state as saved (clears the dirty flag).</summary>
    public void MarkSaved()
    {
        _saved = Current;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static long EstimateBytes(DocumentState? previous, DocumentState state)
    {
        var known = previous is null
            ? new HashSet<Imaging.Tile>(ReferenceEqualityComparer.Instance)
            : new HashSet<Imaging.Tile>(previous.Tiles, ReferenceEqualityComparer.Instance);
        long bytes = 0;
        foreach (var t in state.Tiles)
        {
            if (known.Add(t))
            {
                bytes += t.ByteSize;
            }
        }

        return bytes + (state.TextCount * TextObjectBytes) + 4096;
    }

    private void Evict()
    {
        var total = TotalBytes;
        while (total > BudgetBytes && _index > 1)
        {
            // Drop the baseline; entry 1 becomes the new baseline and its incremental cost no longer counts.
            _entries.RemoveAt(0);
            total -= _entries[0].Bytes;
            _entries[0] = _entries[0] with { Bytes = 0 };
            _index--;
        }
    }
}
