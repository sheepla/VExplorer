namespace VExplorer.Core.FileSystem;

/// <summary>
/// A deleted item inside the Recycle Bin. Identified by an opaque
/// <see cref="Token"/> that the source maps back to its live shell item for
/// restore / permanent-delete; the PIDL itself never leaves the Shell layer.
/// </summary>
public readonly record struct RecycleBinItem(
    int Token,
    string DisplayName,
    string OriginalPath,
    DateTime? DeletedUtc,
    long? SizeBytes,
    bool IsDirectory
);

/// <summary>
/// Reads and operates on the Windows Recycle Bin via the shell namespace.
/// Enumeration is a separate path from the filesystem lister (deleted items have
/// no live filesystem path). Implemented in the Shell layer.
/// </summary>
public interface IRecycleBinSource
{
    /// <summary>Snapshot of the Recycle Bin's current contents.</summary>
    IReadOnlyList<RecycleBinItem> List();

    /// <summary>Restores the given items to their original locations.</summary>
    void Restore(IReadOnlyList<int> tokens);

    /// <summary>Permanently deletes the given items (no further undo).</summary>
    void DeletePermanently(IReadOnlyList<int> tokens, nint ownerHwnd);

    /// <summary>Empties the entire Recycle Bin.</summary>
    void Empty(nint ownerHwnd);
}
