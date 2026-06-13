namespace VExplorer.Core.Completion;

/// <summary>
/// Candidate matching shared by completion and SEARCH/FILTER. Default is
/// substring + smartcase; <c>fuzzy</c> switches to subsequence matching
/// (the <c>:set fuzzy</c> toggle).
/// </summary>
public static class CompletionMatcher
{
    /// <summary>
    /// Returns whether <paramref name="candidate"/> matches <paramref name="query"/>.
    /// <para>
    /// Smartcase: an all-lowercase query matches case-insensitively; a query
    /// containing any uppercase character matches case-sensitively. An empty
    /// query matches everything.
    /// </para>
    /// </summary>
    public static bool IsMatch(string candidate, string query, bool fuzzy = false)
    {
        if (query.Length == 0)
        {
            return true;
        }

        bool caseSensitive = HasUpper(query);
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return fuzzy
            ? IsSubsequence(candidate, query, caseSensitive)
            : candidate.Contains(query, comparison);
    }

    /// <summary>
    /// Fuzzy match: every character of <paramref name="query"/> occurs in
    /// <paramref name="candidate"/> in order (not necessarily contiguously).
    /// Smartcase mirrors the substring path.
    /// </summary>
    private static bool IsSubsequence(string candidate, string query, bool caseSensitive)
    {
        int qi = 0;
        for (int ci = 0; ci < candidate.Length && qi < query.Length; ci++)
        {
            char c = candidate[ci];
            char q = query[qi];
            bool same = caseSensitive
                ? c == q
                : char.ToLowerInvariant(c) == char.ToLowerInvariant(q);
            if (same)
            {
                qi++;
            }
        }
        return qi == query.Length;
    }

    private static bool HasUpper(string value)
    {
        foreach (char c in value)
        {
            if (char.IsUpper(c))
            {
                return true;
            }
        }
        return false;
    }
}
