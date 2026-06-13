namespace VExplorer.Core.Completion;

/// <summary>
/// A candidate source for one <see cref="CompletionContextKind"/>. The engine
/// selects providers by <see cref="Kind"/> based on the resolved context.
/// </summary>
public interface ICompletionProvider
{
    CompletionContextKind Kind { get; }

    IAsyncEnumerable<CompletionCandidate> GetCandidatesAsync(
        CompletionContext context,
        CancellationToken cancel
    );
}
