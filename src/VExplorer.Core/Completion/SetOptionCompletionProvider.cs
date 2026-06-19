using System.Runtime.CompilerServices;
using VExplorer.Core.State;

namespace VExplorer.Core.Completion;

/// <summary>
/// Completes <c>:set</c> option names (hidden, foldersfirst, columns, …) and, after
/// an <c>=</c>, the option's fixed value set (e.g. <c>theme=dark|light|system</c>),
/// both from <see cref="SettingsCommand"/> so completion and the command always agree.
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

        // After "key=", complete the value from that option's fixed value set
        // (e.g. ":set theme=" → system / light / dark). The whole "key=value"
        // token is replaced so the inserted text stays a valid :set argument.
        int eq = context.Token.IndexOf('=');
        if (eq >= 0)
        {
            string key = context.Token[..eq];
            string valuePrefix = context.Token[(eq + 1)..];
            foreach (string value in SettingsCommand.ValuesFor(key))
            {
                cancel.ThrowIfCancellationRequested();
                if (CompletionMatcher.IsMatch(value, valuePrefix))
                {
                    yield return new CompletionCandidate(
                        $"{key}={value}",
                        value,
                        CompletionKind.SetOption
                    );
                }
            }
            yield break;
        }

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
