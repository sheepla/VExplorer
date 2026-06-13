using System.IO;

namespace VExplorer.Core.Completion;

/// <summary>
/// Splits a path token into the directory to enumerate, the prefix to prepend
/// when building a candidate's insertion text, and the leaf used to filter.
/// </summary>
/// <param name="DirectoryPath">Absolute directory to enumerate for candidates.</param>
/// <param name="DirPrefix">
/// The directory portion (absolute, with a single trailing separator) that is
/// prepended to each candidate name, e.g. <c>C:\Users\</c>.
/// </param>
/// <param name="LeafPrefix">The partial leaf name used to filter children.</param>
public readonly record struct PathTokenSplit(
    string DirectoryPath,
    string DirPrefix,
    string LeafPrefix
);

/// <summary>
/// Pure path-token parsing for completion. Handles <c>~</c> expansion, drive
/// roots (<c>C:</c> → <c>C:\</c>) and relative tokens resolved against the
/// current directory. No filesystem access.
/// </summary>
public static class PathTokenSplitter
{
    public static PathTokenSplit Split(string token, string currentDirectory, string homeDirectory)
    {
        // Normalize forward slashes so the whole path (not just the tail) uses
        // the platform separator, then expand a leading "~".
        token = token.Replace('/', Path.DirectorySeparatorChar);
        token = ExpandHome(token, homeDirectory);

        int sep = token.LastIndexOf(Path.DirectorySeparatorChar);
        if (sep >= 0)
        {
            string dirRaw = token[..(sep + 1)];
            string leaf = token[(sep + 1)..];
            string absDir = Path.IsPathRooted(dirRaw)
                ? dirRaw
                : Path.Combine(currentDirectory, dirRaw);
            string prefix = EnsureTrailingSeparator(absDir);
            return new PathTokenSplit(prefix, prefix, leaf);
        }

        // No separator in the token.
        if (IsDriveOnly(token))
        {
            string root = token + Path.DirectorySeparatorChar;
            return new PathTokenSplit(root, root, "");
        }

        // Relative leaf resolved against the current directory.
        string current = EnsureTrailingSeparator(currentDirectory);
        return new PathTokenSplit(current, current, token);
    }

    private static string ExpandHome(string token, string homeDirectory)
    {
        if (token == "~")
        {
            return EnsureTrailingSeparator(homeDirectory);
        }
        if (token.StartsWith("~\\", StringComparison.Ordinal))
        {
            return EnsureTrailingSeparator(homeDirectory) + token[2..];
        }
        return token;
    }

    private static bool IsDriveOnly(string token)
    {
        return token.Length == 2 && char.IsLetter(token[0]) && token[1] == ':';
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.Length == 0)
        {
            return path;
        }
        char last = path[^1];
        if (last is '\\' or '/')
        {
            // Normalize a trailing forward slash to the platform separator.
            return path[..^1] + Path.DirectorySeparatorChar;
        }
        return path + Path.DirectorySeparatorChar;
    }
}
