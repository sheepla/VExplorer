namespace VExplorer.Core.Completion;

/// <summary>The structural level a UNC token currently sits at.</summary>
public enum UncTokenKind
{
    /// <summary>Not a UNC token (does not start with <c>\\</c>).</summary>
    None,

    /// <summary>At the server segment: <c>\\</c> or <c>\\partialserver</c>.</summary>
    Server,

    /// <summary>At the share segment: <c>\\server\</c> or <c>\\server\partialshare</c>.</summary>
    Share,

    /// <summary>Below a share (<c>\\server\share\...</c>); normal path completion applies.</summary>
    Path,
}

/// <summary>
/// The classified UNC token: its <see cref="UncTokenKind"/>, the resolved server
/// (for share-level tokens) and the partial leaf used to filter candidates.
/// </summary>
/// <param name="Kind">Which UNC segment the caret sits in.</param>
/// <param name="Server">The server name, set only for <see cref="UncTokenKind.Share"/>.</param>
/// <param name="Prefix">
/// The partial leaf to filter by: the partial server name for
/// <see cref="UncTokenKind.Server"/>, the partial share name for
/// <see cref="UncTokenKind.Share"/>. Empty otherwise.
/// </param>
public readonly record struct UncToken(UncTokenKind Kind, string Server, string Prefix);

/// <summary>
/// Pure parsing that locates which UNC segment a path token sits in. No
/// filesystem access. Server and share segments are completed by
/// <see cref="UncPathCompletionProvider"/>; everything below a share defers to
/// the ordinary <see cref="PathCompletionProvider"/>.
/// </summary>
public static class UncTokenClassifier
{
    public static UncToken Classify(string token)
    {
        // Accept forward slashes the same way the rest of completion does.
        string normalized = token.Replace('/', '\\');
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return new UncToken(UncTokenKind.None, "", "");
        }

        string rest = normalized[2..];
        int firstSeparator = rest.IndexOf('\\');
        if (firstSeparator < 0)
        {
            // "\\" or "\\server" — still typing the server name.
            return new UncToken(UncTokenKind.Server, "", rest);
        }

        string server = rest[..firstSeparator];
        string afterServer = rest[(firstSeparator + 1)..];
        if (afterServer.IndexOf('\\') < 0)
        {
            // "\\server\" or "\\server\share" — still typing the share name.
            return new UncToken(UncTokenKind.Share, server, afterServer);
        }

        // "\\server\share\..." — a real directory path; ordinary completion handles it.
        return new UncToken(UncTokenKind.Path, server, "");
    }
}
