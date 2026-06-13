using System.IO;

namespace VExplorer.Core.FileSystem;

/// <summary>
/// Pure path resolution and normalization (no filesystem access). Expands
/// <c>~</c>, resolves relative paths against a base directory, and normalizes
/// separators / <c>..</c> / duplicate separators via <see cref="Path.GetFullPath(string,string)"/>.
/// </summary>
public static class PathResolver
{
    /// <summary>
    /// Resolves <paramref name="input"/> to a normalized absolute path:
    /// trims surrounding whitespace and quotes, expands a leading <c>~</c> to
    /// <paramref name="homeDirectory"/>, and resolves a relative path against
    /// <paramref name="baseDirectory"/>. Returns the trimmed input unchanged if
    /// normalization fails (the caller validates existence).
    /// </summary>
    public static string Resolve(string input, string baseDirectory, string homeDirectory)
    {
        string path = input.Trim().Trim('"');
        if (path.Length == 0)
        {
            return path;
        }

        path = ExpandHome(path, homeDirectory);

        try
        {
            // GetFullPath(path, basePath) is pure string normalization: it does
            // not touch the disk. It collapses "..", duplicate separators and
            // unifies "/" to the platform separator.
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(path, baseDirectory);
        }
        catch (ArgumentException)
        {
            return path;
        }
        catch (NotSupportedException)
        {
            return path;
        }
        catch (PathTooLongException)
        {
            return path;
        }
    }

    /// <summary>Normalizes an already-absolute path; trims a redundant trailing separator.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }
        try
        {
            string full = Path.GetFullPath(path);
            return TrimTrailingSeparator(full);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    /// <summary>Removes a trailing separator unless the path is a drive/UNC root.</summary>
    public static string TrimTrailingSeparator(string path)
    {
        if (path.Length <= 3)
        {
            // "C:\" and shorter — leave as-is.
            return path;
        }
        char last = path[^1];
        return last is '\\' or '/' ? path[..^1] : path;
    }

    private static string ExpandHome(string path, string homeDirectory)
    {
        if (path == "~")
        {
            return homeDirectory;
        }
        if (
            path.StartsWith("~\\", StringComparison.Ordinal)
            || path.StartsWith("~/", StringComparison.Ordinal)
        )
        {
            return Path.Combine(homeDirectory, path[2..]);
        }
        return path;
    }
}
