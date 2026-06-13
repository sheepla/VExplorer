namespace VExplorer.Core.FileSystem;

/// <summary>
/// Shell-resolved metadata for a single file or folder, expressed as UI-agnostic
/// primitives. <see cref="SystemIconIndex"/> is the entry's index in the system
/// image list; it is a stable dedup key for caching the materialized icon (two
/// entries with the same index share one icon).
/// </summary>
public readonly record struct ShellItemInfo(string TypeName, int SystemIconIndex);

/// <summary>
/// Resolves Windows shell metadata (friendly type name, system icon) for file
/// system entries. Implemented over the Windows shell API; kept UI-agnostic so it
/// lives in Core (no WPF / <c>ImageSource</c> dependency).
/// </summary>
public interface IShellInfoProvider
{
    /// <summary>
    /// Resolves the friendly type name and system icon index for an entry.
    /// Ordinary files/folders are resolved by attribute only (no disk I/O) and
    /// cached; entries whose icon is per-instance (executables, shortcuts, drive
    /// roots) are resolved from the real path.
    /// </summary>
    ShellItemInfo Resolve(string fullPath, string extension, bool isDirectory);

    /// <summary>
    /// Resolves the type name and system icon index for a <see cref="Location"/>.
    /// Filesystem locations use the path-based fast path; shell-namespace locations
    /// (PC, special folders, Recycle Bin, Network) resolve via their PIDL so the
    /// real Explorer icon is used.
    /// </summary>
    ShellItemInfo Resolve(Location location, string extension, bool isDirectory);

    /// <summary>
    /// Retrieves the small (16px) shell icon for an entry as a fresh <c>HICON</c>.
    /// The caller owns the handle and must release it with <see cref="DestroyIcon"/>.
    /// Resolution mirrors <see cref="Resolve"/> (by attribute for cacheable
    /// entries, by real path for per-instance icons).
    /// </summary>
    nint CopyIcon(string fullPath, string extension, bool isDirectory);

    /// <summary>
    /// <see cref="CopyIcon(string,string,bool)"/> for a <see cref="Location"/>:
    /// shell-namespace locations are resolved via their PIDL.
    /// </summary>
    nint CopyIcon(Location location, string extension, bool isDirectory);

    /// <summary>Releases an <c>HICON</c> previously returned by <see cref="CopyIcon"/>.</summary>
    void DestroyIcon(nint hIcon);
}
