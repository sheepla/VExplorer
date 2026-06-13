using System.Runtime.CompilerServices;
using VExplorer.Core.State;

namespace VExplorer.Core.Completion;

/// <summary>
/// Completes <c>:set</c> option names (hidden, foldersfirst, columns, …) from the
/// single option table in <see cref="SettingsCommand"/>, so completion and the
/// command always agree on what is settable.
/// </summary>
public sealed class SetOptionCompletionProvider : ICompletionProvider
{
    public CompletionContextKind Kind => CompletionContextKind.SetOption;

    public async IAsyncEnumerable<CompletionCandidate> GetCandidatesAsync(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken cancel
    )
    {
        await Task.CompletedTask; // keep the async-iterator signature uniform

        foreach (string name in SettingsCommand.OptionNames)
        {
            cancel.ThrowIfCancellationRequested();
            if (CompletionMatcher.IsMatch(name, context.Token))
            {
                yield return new CompletionCandidate(name, name, CompletionKind.SetOption);
            }
        }
    }
}
