namespace VExplorer.Core.FileSystem;

/// <summary>
/// Tuning knobs for a single directory listing. The default value (no filter,
/// unlimited time) preserves the original <c>ListAsync</c> behaviour, so callers
/// that pass <c>default</c> are unaffected.
/// </summary>
public readonly record struct ListOptions
{
    /// <summary>
    /// When non-null, only entries whose name matches this query are materialized
    /// (applied during enumeration so non-matches are never built). Used by FILTER
    /// to scan a whole folder cheaply, even one too large to list in full.
    /// </summary>
    public string? NameFilter { get; init; }

    /// <summary>
    /// Time budget (ms) before the enumeration stops and returns what it has so far
    /// (marked <see cref="IFileItemSource.IsTruncated"/>). 0 = unlimited.
    /// </summary>
    public int TimeoutMs { get; init; }

    /// <summary>No filter, unlimited time — equivalent to <c>default</c>.</summary>
    public static ListOptions None => default;
}
