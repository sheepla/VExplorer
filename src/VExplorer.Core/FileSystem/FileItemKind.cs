namespace VExplorer.Core.FileSystem;

/// <summary>
/// The role of a <see cref="FileItem"/> row, used to pick its icon and to decide
/// activation behavior. Most items are ordinary <see cref="Physical"/> files and
/// folders; the others appear only in the synthetic "PC" listing.
/// </summary>
public enum FileItemKind
{
    /// <summary>An ordinary file or directory on disk (the default).</summary>
    Physical,

    /// <summary>A physical drive root (💾, e.g. <c>C:\</c>) shown under the PC root.</summary>
    Drive,

    /// <summary>
    /// A special folder alias (🔗, e.g. Documents) whose <see cref="FileItem.FullPath"/>
    /// is its physical target. Opening it jumps into the physical tree.
    /// </summary>
    Alias,

    /// <summary>
    /// A virtual destination (🗑, e.g. Recycle Bin, Network) with no physical path.
    /// Opening it shows its own contents in place.
    /// </summary>
    Virtual,
}
