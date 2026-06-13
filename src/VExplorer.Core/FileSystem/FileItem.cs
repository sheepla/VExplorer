namespace VExplorer.Core.FileSystem;

public readonly record struct FileItem
{
    public string Name { get; init; }
    public string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public string Extension { get; init; }

    /// <summary>Has the Hidden attribute (shown dimmed, like Explorer, when visible).</summary>
    public bool IsHidden { get; init; }

    /// <summary>Has the System attribute (shown dimmed, like Explorer, when visible).</summary>
    public bool IsSystem { get; init; }

    /// <summary>The row's role (drive / alias / virtual). Defaults to <see cref="FileItemKind.Physical"/>.</summary>
    public FileItemKind Kind { get; init; }

    /// <summary>
    /// Optional display label that overrides <see cref="Name"/> (e.g. a localized
    /// special-folder name). <c>null</c> falls back to <see cref="Name"/>.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Explicit navigation identity for shell-namespace rows (PC-root entries:
    /// special folders, Recycle Bin, Network). <c>null</c> for ordinary filesystem
    /// rows — the overwhelmingly common case adds no allocation here.
    /// </summary>
    public Location? Identity { get; init; }

    /// <summary>The row's navigable location: its <see cref="Identity"/>, else its filesystem path.</summary>
    public Location ResolveLocation()
    {
        return Identity ?? Location.ForPath(FullPath);
    }

    /// <summary>
    /// Opaque token identifying this row within a shell source (e.g. a Recycle Bin
    /// item, for restore / permanent-delete). <c>null</c> for ordinary rows.
    /// </summary>
    public int? ShellToken { get; init; }
}
