namespace VExplorer.Core.FileSystem;

/// <summary>
/// Well-known user folders that the address bar can complete by name.
/// Restricted to the profile-based "place" folders (役割A / alias) from
/// <c>VExplorer_SpecialFolders.md</c>; virtual folders (Recycle Bin, Network)
/// have no physical path and are handled elsewhere.
/// </summary>
public enum KnownFolder
{
    Home,
    Desktop,
    Documents,
    Downloads,
    Pictures,
    Music,
    Videos,
}

/// <summary>
/// A profile "place" special folder to show under the PC root: an English
/// (on-disk) <paramref name="DisplayName"/> (e.g. "Documents"), its physical path,
/// and the <c>KNOWNFOLDERID</c> it came from (used as a shell identity and to
/// fetch the real Explorer icon).
/// </summary>
public readonly record struct SpecialFolderEntry(
    string DisplayName,
    string PhysicalPath,
    Guid KnownFolderId
);

/// <summary>
/// Resolves environment-dependent paths (user home, known folders) for
/// completion. Implemented in the Shell layer so the Core stays free of
/// direct environment access and remains unit-testable with a fake.
/// </summary>
public interface ISpecialFolderSource
{
    /// <summary>The current user's home directory (used to expand <c>~</c>).</summary>
    string GetHomeDirectory();

    /// <summary>
    /// The physical path of <paramref name="folder"/>, or <c>null</c> when the
    /// folder is not defined on this machine.
    /// </summary>
    string? Resolve(KnownFolder folder);

    /// <summary>
    /// The profile "place" special folders to list under the PC root (Home plus
    /// the profile-direct folders), with environment clutter filtered out.
    /// Ordered for display; de-duplicated by physical path.
    /// </summary>
    IReadOnlyList<SpecialFolderEntry> EnumeratePlaces();
}
