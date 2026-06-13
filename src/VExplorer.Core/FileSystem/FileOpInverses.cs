using System.IO;

namespace VExplorer.Core.FileSystem;

/// <summary>
/// Pure helpers that derive the data needed to reverse a file operation. Kept in
/// Core (no shell dependency) so the path math is unit-testable in isolation.
/// </summary>
public static class FileOpInverses
{
    /// <summary>
    /// For a rename of <paramref name="sourcePath"/> to <paramref name="newName"/>:
    /// the resulting full path and the original leaf name (to rename back).
    /// </summary>
    public static (string NewPath, string OldName) DeriveRenameInverse(
        string sourcePath,
        string newName
    )
    {
        string trimmed = sourcePath.TrimEnd(Path.DirectorySeparatorChar);
        string parent = Path.GetDirectoryName(trimmed) ?? "";
        string oldName = Path.GetFileName(trimmed);
        string newPath = Path.Combine(parent, newName);
        return (newPath, oldName);
    }

    /// <summary>The full path a folder created via <c>mkdir</c> would have.</summary>
    public static string DeriveCreatedFolderPath(string directory, string name)
    {
        return Path.Combine(directory.TrimEnd(Path.DirectorySeparatorChar), name);
    }

    /// <summary>
    /// For a move of <paramref name="sources"/> into <paramref name="destDir"/>
    /// (no auto-rename): the resulting destination paths grouped by each item's
    /// original parent directory, so undo can move each group back.
    /// </summary>
    public static IReadOnlyList<(
        string SourceParent,
        IReadOnlyList<string> DestPaths
    )> DeriveMovedDestPaths(IReadOnlyList<string> sources, string destDir)
    {
        string dest = destDir.TrimEnd(Path.DirectorySeparatorChar);
        Dictionary<string, List<string>> byParent = new(StringComparer.OrdinalIgnoreCase);
        List<string> order = [];

        foreach (string source in sources)
        {
            string trimmed = source.TrimEnd(Path.DirectorySeparatorChar);
            string parent = Path.GetDirectoryName(trimmed) ?? "";
            string leaf = Path.GetFileName(trimmed);
            string destPath = Path.Combine(dest, leaf);

            if (!byParent.TryGetValue(parent, out List<string>? list))
            {
                list = [];
                byParent[parent] = list;
                order.Add(parent);
            }
            list.Add(destPath);
        }

        return order.Select(p => (p, (IReadOnlyList<string>)byParent[p])).ToList();
    }
}
