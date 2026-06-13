using VExplorer.Core.Completion;

namespace VExplorer.Core.FileSystem;

/// <summary>
/// Pure FILTER + sort over lightweight <see cref="FileItem"/> values (no UI / row
/// objects). The file list operates on this layer so narrowing and ordering a huge
/// folder never has to build a display row per entry. Directories always sort above
/// files (Explorer-style); the column sort applies within each group with a Name
/// tie-break. Mirrors the ordering the view used to compute over display rows.
/// </summary>
public static class FileListProjection
{
    /// <summary>
    /// Returns <paramref name="items"/> narrowed by <paramref name="filter"/> (when
    /// non-empty, smartcase substring on the display label) and ordered by
    /// <paramref name="sortColumn"/> ("Name"/"Size"/"Modified"/"Type").
    /// <paramref name="typeKey"/> supplies the Type-column sort key (the shell type
    /// name); when null a cheap extension-based fallback is used.
    /// </summary>
    public static List<FileItem> SortAndFilter(
        IReadOnlyList<FileItem> items,
        string? filter,
        string sortColumn,
        bool descending,
        Func<FileItem, string>? typeKey = null,
        bool fuzzy = false,
        bool foldersFirst = true
    )
    {
        IEnumerable<FileItem> seq = items;
        if (!string.IsNullOrEmpty(filter))
        {
            string f = filter;
            seq = seq.Where(it => CompletionMatcher.IsMatch(it.DisplayName ?? it.Name, f, fuzzy));
        }

        StringComparer oic = StringComparer.OrdinalIgnoreCase;
        // Group directories above files unless folders-first is disabled (:set
        // nofoldersfirst), in which case the column sort applies across all items.
        IOrderedEnumerable<FileItem> byKind = foldersFirst
            ? seq.OrderByDescending(it => it.IsDirectory)
            : seq.OrderBy(_ => 0);
        IOrderedEnumerable<FileItem> sorted = sortColumn switch
        {
            "Size" => descending
                ? byKind.ThenByDescending(SortableSize).ThenBy(it => it.Name, oic)
                : byKind.ThenBy(SortableSize).ThenBy(it => it.Name, oic),
            "Modified" => descending
                ? byKind.ThenByDescending(it => it.LastWriteTimeUtc).ThenBy(it => it.Name, oic)
                : byKind.ThenBy(it => it.LastWriteTimeUtc).ThenBy(it => it.Name, oic),
            "Type" => descending
                ? byKind.ThenByDescending(TypeKey, oic).ThenBy(it => it.Name, oic)
                : byKind.ThenBy(TypeKey, oic).ThenBy(it => it.Name, oic),
            _ => descending
                ? byKind.ThenByDescending(it => it.Name, oic)
                : byKind.ThenBy(it => it.Name, oic),
        };

        return sorted.ToList();

        static long SortableSize(FileItem it)
        {
            return it.IsDirectory ? -1L : it.SizeBytes ?? 0L;
        }

        string TypeKey(FileItem it)
        {
            return typeKey?.Invoke(it) ?? FallbackTypeLabel(it);
        }
    }

    /// <summary>
    /// Cheap extension-based type label used as the Type sort key until the shell
    /// name is supplied: "Folder", "{EXT} File" (e.g. "TXT File"), or "File".
    /// </summary>
    public static string FallbackTypeLabel(FileItem item)
    {
        if (item.IsDirectory)
        {
            return "Folder";
        }
        string ext = item.Extension.TrimStart('.');
        return ext.Length > 0 ? $"{ext.ToUpperInvariant()} File" : "File";
    }
}
