using System.Runtime.CompilerServices;
using VExplorer.Core.FileSystem;

namespace VExplorer.Core.Completion;

/// <summary>
/// Completes with the current item's name. Used for <c>:rename</c> so Tab inserts
/// the existing name, ready to edit a part of it. Yields at most one candidate
/// and only when it matches what the user has typed so far.
/// </summary>
public sealed class CurrentNameCompletionProvider(ICurrentItemSource currentItem)
    : ICompletionProvider
{
    private readonly ICurrentItemSource _currentItem = currentItem;

    public CompletionContextKind Kind => CompletionContextKind.CurrentName;

    public async IAsyncEnumerable<CompletionCandidate> GetCandidatesAsync(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken cancel
    )
    {
        await Task.CompletedTask;

        string? name = _currentItem.GetCurrentItemName();
        if (string.IsNullOrEmpty(name))
        {
            yield break;
        }

        // Offer the name when the token is empty or a prefix of it (smartcase).
        if (CompletionMatcher.IsMatch(name, context.Token))
        {
            yield return new CompletionCandidate(name, name, CompletionKind.File);
        }
    }
}
