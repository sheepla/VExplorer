using System.Runtime.CompilerServices;
using VExplorer.Core.Completion;

namespace VExplorer.Core.Commands;

/// <summary>
/// Completes command names. Empty input lists all commands; any input narrows
/// to a prefix match. Results are stable-sorted alphabetically so a given
/// prefix always yields the same order (frequency weighting is a future hook).
/// Accepting a candidate appends a space, ready for arguments.
/// </summary>
public sealed class CommandNameCompletionProvider(CommandRegistry registry) : ICompletionProvider
{
    private readonly CommandRegistry _registry = registry;

    public CompletionContextKind Kind => CompletionContextKind.CommandName;

    public async IAsyncEnumerable<CompletionCandidate> GetCandidatesAsync(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken cancel
    )
    {
        await Task.CompletedTask;

        string query = context.Token;
        bool caseSensitive = query.Any(char.IsUpper);
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        IEnumerable<CommandSpec> matches = _registry
            .Commands.Where(s => s.Name.StartsWith(query, comparison))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

        foreach (CommandSpec spec in matches)
        {
            cancel.ThrowIfCancellationRequested();
            // Trailing space positions the caret to type arguments and lets a
            // further Tab trigger argument completion.
            yield return new CompletionCandidate(
                spec.Name + " ",
                spec.Name,
                CompletionKind.Command
            );
        }
    }
}
