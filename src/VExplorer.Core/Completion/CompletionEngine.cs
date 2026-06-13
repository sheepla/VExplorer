namespace VExplorer.Core.Completion;

/// <summary>
/// The single entry point for completion. Given a resolved
/// <see cref="CompletionContext"/>, it consults the providers named in
/// <see cref="CompletionContext.Providers"/> (in order), merges their
/// candidates and drops duplicates by insertion text. Stateless.
/// </summary>
public sealed class CompletionEngine
{
    private readonly IReadOnlyDictionary<CompletionContextKind, ICompletionProvider> _providers;

    public CompletionEngine(IEnumerable<ICompletionProvider> providers)
    {
        // Last registration wins per kind; today each kind has one provider.
        Dictionary<CompletionContextKind, ICompletionProvider> map = [];
        foreach (ICompletionProvider provider in providers)
        {
            map[provider.Kind] = provider;
        }
        _providers = map;
    }

    public async IAsyncEnumerable<CompletionCandidate> GetCandidatesAsync(
        CompletionContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel
    )
    {
        // Case-insensitive: paths differing only in case are the same candidate.
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (CompletionContextKind kind in context.Providers)
        {
            if (!_providers.TryGetValue(kind, out ICompletionProvider? provider))
            {
                continue;
            }

            await foreach (
                CompletionCandidate candidate in provider.GetCandidatesAsync(context, cancel)
            )
            {
                if (seen.Add(candidate.InsertionText))
                {
                    yield return candidate;
                }
            }
        }
    }
}
