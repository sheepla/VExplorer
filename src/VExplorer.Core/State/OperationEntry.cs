namespace VExplorer.Core.State;

/// <summary>
/// One reversible file operation on the undo/redo stack. <see cref="Undo"/> and
/// <see cref="Redo"/> perform the inverse / re-apply and return <c>false</c> when
/// they fail or are cancelled (so the history pointer is not advanced).
/// </summary>
public sealed record OperationEntry(
    string Label,
    Func<CancellationToken, Task<bool>> Undo,
    Func<CancellationToken, Task<bool>> Redo
);
