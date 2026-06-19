using System.Runtime.CompilerServices;
using VExplorer.Core.FileSystem;

namespace VExplorer.Core.Completion;

/// <summary>
/// Completes the two UNC segments that ordinary path completion cannot reach:
/// the server (<c>\\</c> / <c>\\partial</c>, served from network discovery) and
/// the share (<c>\\server\</c> / <c>\\server\partial</c>, served from the share
/// source). Below a share (<c>\\server\share\...</c>) it yields nothing and lets
/// <see cref="PathCompletionProvider"/> take over. Candidates carry a trailing
/// separator so partial-input + Tab chains downward, matching path completion.
/// </summary>
public sealed class UncPathCompletionProvider(INetworkSource network, IUncShareSource shares)
    : ICompletionProvider
{
    private readonly INetworkSource _network = network;
    private readonly IUncShareSource _shares = shares;

    public CompletionContextKind Kind => CompletionContextKind.UncPath;

    public async IAsyncEnumerable<CompletionCandidate> GetCandidatesAsync(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken cancel
    )
    {
        UncToken token = UncTokenClassifier.Classify(context.Token);
        if (token.Kind is UncTokenKind.None or UncTokenKind.Path)
        {
            yield break;
        }

        // Server / share discovery is network I/O; run it off the UI thread.
        List<CompletionCandidate> matches = await Task.Run(() => Collect(token, cancel), cancel);
        foreach (CompletionCandidate candidate in matches)
        {
            yield return candidate;
        }
    }

    private List<CompletionCandidate> Collect(UncToken token, CancellationToken cancel)
    {
        List<CompletionCandidate> matches = [];

        if (token.Kind == UncTokenKind.Server)
        {
            foreach (NetworkEntry entry in _network.List())
            {
                cancel.ThrowIfCancellationRequested();
                if (!entry.IsContainer)
                {
                    continue;
                }
                // The host name (not the friendly display name) is what completes
                // into the path, so filter and insert on it.
                string host = entry.UncPath.TrimStart('\\');
                if (host.Length == 0)
                {
                    host = entry.DisplayName;
                }
                if (!CompletionMatcher.IsMatch(host, token.Prefix))
                {
                    continue;
                }
                matches.Add(new CompletionCandidate($@"\\{host}\", host, CompletionKind.Folder));
            }
        }
        else // UncTokenKind.Share
        {
            foreach (UncShareEntry share in _shares.ListShares(token.Server))
            {
                cancel.ThrowIfCancellationRequested();
                if (!CompletionMatcher.IsMatch(share.Name, token.Prefix))
                {
                    continue;
                }
                string insertion = share.UncPath.EndsWith('\\')
                    ? share.UncPath
                    : share.UncPath + '\\';
                matches.Add(new CompletionCandidate(insertion, share.Name, CompletionKind.Folder));
            }
        }

        matches.Sort(
            (a, b) =>
                string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase)
        );
        return matches;
    }
}
