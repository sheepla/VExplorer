using System.IO;
using System.Runtime.CompilerServices;
using VExplorer.Core.Completion;
using VExplorer.Core.FileSystem;

namespace VExplorer.App.Features.Completion;

/// <summary>
/// Offers the active tab's previously-visited locations as ADDRESS-bar
/// completion candidates, newest first. Lives in the App layer because it reads
/// the active tab's history via <see cref="TabManager"/>; insertion text is the
/// location's parsing name so it round-trips through the address bar's resolver.
/// </summary>
public sealed class NavigationHistoryCompletionProvider(TabManager tabManager) : ICompletionProvider
{
    private readonly TabManager _tabManager = tabManager;

    public CompletionContextKind Kind => CompletionContextKind.NavigationHistory;

    public async IAsyncEnumerable<CompletionCandidate> GetCandidatesAsync(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken cancel
    )
    {
        await Task.CompletedTask; // keep the async-iterator signature uniform

        IReadOnlyList<Location> history = _tabManager.GetActiveTabState().History;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        // Newest first.
        for (int i = history.Count - 1; i >= 0; i--)
        {
            cancel.ThrowIfCancellationRequested();

            Location loc = history[i];
            // Append a trailing separator for filesystem dirs so the insertion text
            // matches the Path provider's (which appends one), letting the engine
            // de-dupe the same folder across providers.
            string insertion;
            if (loc.TryGetFileSystemPath(out string path))
            {
                insertion =
                    path.Length > 0 && path[^1] is not ('\\' or '/')
                        ? path + Path.DirectorySeparatorChar
                        : path;
            }
            else
            {
                insertion = loc.ParsingName;
            }
            if (insertion.Length == 0 || !seen.Add(insertion))
            {
                continue;
            }

            string display = loc.DisplayName;
            if (
                !CompletionMatcher.IsMatch(display, context.Token)
                && !CompletionMatcher.IsMatch(insertion, context.Token)
            )
            {
                continue;
            }

            CompletionKind kind = loc.IsShell
                ? CompletionKind.SpecialFolder
                : CompletionKind.Folder;
            yield return new CompletionCandidate(insertion, display, kind);
        }
    }
}
