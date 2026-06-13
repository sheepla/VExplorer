namespace VExplorer.Core.FileSystem;

/// <summary>Whether a <see cref="Location"/> is a filesystem path or a shell-namespace item.</summary>
public enum LocationKind
{
    FileSystem,
    Shell,
}

/// <summary>
/// A navigable location: either a physical filesystem path or a Windows shell
/// namespace item (This PC, a special folder, Recycle Bin, Network).
///
/// <para>
/// This mirrors how Explorer identifies locations. The filesystem case is a thin
/// wrapper over the absolute path string (the fast, common path — no extra
/// allocation, every existing path consumer keeps working via
/// <see cref="TryGetFileSystemPath"/>). The shell case carries a durable
/// <see cref="ParsingName"/> (resolvable to a PIDL with <c>SHParseDisplayName</c>)
/// plus an optional <c>KNOWNFOLDERID</c>; the binary PIDL itself is never stored
/// here — the Shell layer materializes and frees it on demand.
/// </para>
/// </summary>
public readonly struct Location : IEquatable<Location>
{
    private readonly string? _value;
    private readonly string? _displayName;

    private Location(LocationKind kind, string value, string? displayName, Guid knownFolderId)
    {
        Kind = kind;
        _value = value;
        _displayName = displayName;
        KnownFolderId = knownFolderId;
    }

    public LocationKind Kind { get; }

    /// <summary>The <c>KNOWNFOLDERID</c> for shell items that have one; otherwise <see cref="Guid.Empty"/>.</summary>
    public Guid KnownFolderId { get; }

    public bool IsShell => Kind == LocationKind.Shell;

    /// <summary>A filesystem location for an absolute path.</summary>
    public static Location ForPath(string absolutePath)
    {
        return new(LocationKind.FileSystem, absolutePath ?? "", null, Guid.Empty);
    }

    /// <summary>
    /// A shell-namespace location identified by a durable <paramref name="parsingName"/>
    /// (e.g. <c>::{CLSID}</c> or <c>shell:Documents</c>), with an optional display
    /// label and <c>KNOWNFOLDERID</c> used for icon resolution.
    /// </summary>
    public static Location ForShell(
        string parsingName,
        string? displayName = null,
        Guid knownFolderId = default
    )
    {
        return new(LocationKind.Shell, parsingName, displayName, knownFolderId);
    }

    /// <summary>
    /// True (with the absolute path) when this location is a filesystem path. The
    /// single accessor that lets the many path-based consumers keep working.
    /// </summary>
    public bool TryGetFileSystemPath(out string path)
    {
        if (Kind == LocationKind.FileSystem && !string.IsNullOrEmpty(_value))
        {
            path = _value;
            return true;
        }
        path = "";
        return false;
    }

    /// <summary>The durable identity string: the path for filesystem, the parsing name for shell.</summary>
    public string ParsingName => _value ?? "";

    /// <summary>A human-readable label for the address bar / status.</summary>
    public string DisplayName => _displayName ?? (_value ?? "");

    public bool Equals(Location other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }
        if (Kind == LocationKind.FileSystem)
        {
            return string.Equals(_value, other._value, StringComparison.OrdinalIgnoreCase);
        }
        // Shell: prefer the known-folder id when both have one, else the parsing name.
        if (KnownFolderId != Guid.Empty && other.KnownFolderId != Guid.Empty)
        {
            return KnownFolderId == other.KnownFolderId;
        }
        return string.Equals(_value, other._value, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return obj is Location other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (Kind == LocationKind.Shell && KnownFolderId != Guid.Empty)
        {
            return HashCode.Combine(Kind, KnownFolderId);
        }
        return HashCode.Combine(
            Kind,
            _value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(_value)
        );
    }

    public static bool operator ==(Location left, Location right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Location left, Location right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"{Kind}:{_value}";
    }
}
