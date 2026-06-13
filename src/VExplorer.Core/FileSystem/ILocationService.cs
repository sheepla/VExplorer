namespace VExplorer.Core.FileSystem;

/// <summary>
/// Bridges the shell namespace and the filesystem for navigation: lists the PC
/// root, computes parents (special folders' parent is PC), and resolves a
/// shell-namespace location to a filesystem path for physical listing/operations.
/// Implemented in the Shell layer; kept UI- and Win32-agnostic in Core.
/// </summary>
public interface ILocationService
{
    /// <summary>
    /// The PC root contents: physical drives + special folders + Recycle Bin +
    /// Network, each row carrying its navigation <see cref="FileItem.Identity"/>.
    /// </summary>
    IFileItemSource ListPcRoot();

    /// <summary>
    /// The parent location for "..": PC for drive roots / special folders /
    /// virtual nodes; the physical parent for a filesystem subfolder; <c>null</c>
    /// for the PC root (which has no parent).
    /// </summary>
    Location? GetParent(Location location);

    /// <summary>
    /// True (with the path) when <paramref name="location"/> can be listed/operated
    /// on as a filesystem directory — its own path for filesystem locations, the
    /// resolved physical path for special "place" folders. False for PC / Recycle
    /// Bin / Network (no physical directory).
    /// </summary>
    bool TryGetListingPath(Location location, out string physicalPath);

    /// <summary>
    /// Resolves address-bar / command input to a <see cref="Location"/>. Accepts a
    /// known location name ("PC", "Recycle Bin", "Network"), a Windows shell
    /// parsing name (<c>shell:Documents</c>, <c>::{CLSID}</c>), or an existing
    /// filesystem directory path. Returns false when the input resolves to nothing.
    /// </summary>
    bool TryResolve(string input, out Location location);
}
