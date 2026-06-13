using System.IO;
using VExplorer.Core.FileSystem;

namespace VExplorer.App;

/// <summary>Short, human-readable labels for locations (window title, tab text).</summary>
public static class LocationLabels
{
    /// <summary>The location's leaf folder name, or its shell display name.</summary>
    public static string Folder(Location loc)
    {
        if (loc.TryGetFileSystemPath(out string path))
        {
            string leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            return leaf.Length > 0 ? leaf : path;
        }
        return loc.DisplayName;
    }
}
