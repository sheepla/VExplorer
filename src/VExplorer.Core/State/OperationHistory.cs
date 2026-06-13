namespace VExplorer.Core.State;

/// <summary>
/// In-memory <see cref="IOperationHistory"/>. Two stacks plus a re-entrancy guard:
/// shell operations pump a modal loop, so overlapping undo/redo keypresses are
/// real and must be ignored while one is running. The undo side is a list so the
/// oldest entry can be trimmed when the cap is exceeded.
/// </summary>
public sealed class OperationHistory : IOperationHistory
{
    private const int MaxEntries = 100;

    private readonly List<OperationEntry> _undo = [];
    private readonly Stack<OperationEntry> _redo = new();
    private bool _busy;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(OperationEntry entry)
    {
        _redo.Clear();
        _undo.Add(entry);
        if (_undo.Count > MaxEntries)
        {
            _undo.RemoveAt(0);
        }
    }

    public async Task UndoAsync(CancellationToken cancel = default)
    {
        if (_busy || _undo.Count == 0)
        {
            return;
        }
        _busy = true;
        try
        {
            OperationEntry entry = _undo[^1];
            if (await entry.Undo(cancel))
            {
                _undo.RemoveAt(_undo.Count - 1);
                _redo.Push(entry);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    public async Task RedoAsync(CancellationToken cancel = default)
    {
        if (_busy || _redo.Count == 0)
        {
            return;
        }
        _busy = true;
        try
        {
            OperationEntry entry = _redo.Peek();
            if (await entry.Redo(cancel))
            {
                _redo.Pop();
                _undo.Add(entry);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
