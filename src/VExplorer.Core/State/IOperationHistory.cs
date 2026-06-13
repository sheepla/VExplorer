namespace VExplorer.Core.State;

/// <summary>
/// App-wide undo/redo stack for reversible file operations. Operations push an
/// entry on success; undo/redo run the entry's inverse / re-apply. In-memory only.
/// </summary>
public interface IOperationHistory
{
    bool CanUndo { get; }
    bool CanRedo { get; }

    /// <summary>Records a successfully-performed operation; clears the redo stack.</summary>
    void Push(OperationEntry entry);

    /// <summary>Undoes the most recent operation (no-op when empty or already running).</summary>
    Task UndoAsync(CancellationToken cancel = default);

    /// <summary>Re-applies the most recently undone operation.</summary>
    Task RedoAsync(CancellationToken cancel = default);

    void Clear();
}
