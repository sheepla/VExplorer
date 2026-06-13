using System.Collections.Immutable;

namespace VExplorer.Core.State;

public sealed record Settings
{
    public bool ShowHidden { get; init; } = false;
    public bool ShowSystemFiles { get; init; } = false;
    public bool ShowExtensions { get; init; } = true;

    /// <summary>Show NTFS alternate data streams (<c>:set ads</c>).</summary>
    public bool ShowAds { get; init; } = false;
    public bool FoldersFirst { get; init; } = true;

    /// <summary>Use fuzzy (subsequence) matching for SEARCH / FILTER (<c>:set fuzzy</c>).</summary>
    public bool Fuzzy { get; init; } = false;
    public int TreeFollowDebounceMs { get; init; } = 200;
    public int IncrSearchDelayMs { get; init; } = 150;
    public int AddressBarDelayMs { get; init; } = 100;
    public ImmutableArray<string> Columns { get; init; } = ["name", "size", "mtime", "type"];

    /// <summary>
    /// Time budget (ms) for listing a directory before the enumeration is cut short
    /// and the items gathered so far are shown (marked truncated). 0 = unlimited.
    /// Guards against the UI staying blank on huge folders (e.g. winsxs).
    /// </summary>
    public int ListTimeoutMs { get; init; } = 5000;

    /// <summary>Debounce (ms) for live FILTER input before (re-)listing / narrowing.</summary>
    public int FilterDelayMs { get; init; } = 150;

    /// <summary>
    /// Maximum number of child folders shown under a tree node before the rest are
    /// collapsed behind a "… (N more)" sentinel. Bounds tree work on huge folders.
    /// </summary>
    public int TreeChildrenCap { get; init; } = 200;

    public static Settings Default { get; } = new();
}
