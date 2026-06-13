using System.IO;
using System.IO.Compression;

namespace VExplorer.App.Features.FileOps;

/// <summary>
/// Standard zip create/extract using <see cref="System.IO.Compression"/> (the
/// self-implemented zip from the spec; third-party compressors are reached via
/// shell extensions in the context menu, not here).
/// </summary>
public static class ZipService
{
    /// <summary>
    /// Bundles <paramref name="sources"/> (files and/or directories) into a single
    /// archive at <paramref name="destZipPath"/>. Directory contents are added
    /// recursively with paths relative to their parent. Returns null on success or
    /// an error message.
    /// </summary>
    public static string? Create(IReadOnlyList<string> sources, string destZipPath)
    {
        try
        {
            using FileStream fs = new(destZipPath, FileMode.CreateNew);
            using ZipArchive archive = new(fs, ZipArchiveMode.Create);
            foreach (string source in sources)
            {
                string trimmed = source.TrimEnd(Path.DirectorySeparatorChar);
                if (Directory.Exists(trimmed))
                {
                    string baseDir = Path.GetDirectoryName(trimmed) ?? trimmed;
                    foreach (
                        string file in Directory.EnumerateFiles(
                            trimmed,
                            "*",
                            SearchOption.AllDirectories
                        )
                    )
                    {
                        string entry = Path.GetRelativePath(baseDir, file)
                            .Replace(Path.DirectorySeparatorChar, '/');
                        archive.CreateEntryFromFile(file, entry);
                    }
                }
                else if (File.Exists(trimmed))
                {
                    archive.CreateEntryFromFile(trimmed, Path.GetFileName(trimmed));
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Extracts <paramref name="zipPath"/> into a folder named after the archive
    /// under <paramref name="destDirectory"/> (Explorer-style, to avoid clobbering
    /// the current folder). <paramref name="createdFolderName"/> receives the leaf
    /// name of the created folder (for post-op cursor focus). Returns null on
    /// success or an error message.
    /// </summary>
    public static string? Extract(
        string zipPath,
        string destDirectory,
        out string createdFolderName
    )
    {
        createdFolderName = "";
        try
        {
            string name = Path.GetFileNameWithoutExtension(zipPath);
            string target = Path.Combine(destDirectory, name);
            target = UniqueDirectory(target);
            ZipFile.ExtractToDirectory(zipPath, target);
            createdFolderName = Path.GetFileName(target);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Returns <paramref name="path"/> or "path (2)", "path (3)", … if it exists.</summary>
    private static string UniqueDirectory(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return path;
        }
        for (int i = 2; ; i++)
        {
            string candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
