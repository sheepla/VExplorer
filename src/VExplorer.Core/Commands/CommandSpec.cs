using System.Collections.Immutable;
using VExplorer.Core.Completion;

namespace VExplorer.Core.Commands;

/// <summary>
/// Definition of an internal command. Carries the completion metadata that lets
/// the completion engine work without command-specific logic: each argument
/// position declares which candidate source completes it.
/// </summary>
/// <param name="Name">The command name without the leading <c>:</c> (e.g. "cd").</param>
/// <param name="ArgumentKinds">
/// Completion kind per argument position (0-based). Position 0 is the first
/// argument after the name.
/// </param>
/// <param name="LastArgumentRepeats">
/// When true, argument positions beyond <see cref="ArgumentKinds"/> reuse the
/// last declared kind (for variadic commands like <c>:cp SRC... DEST</c>).
/// </param>
public sealed record CommandSpec(
    string Name,
    ImmutableArray<CompletionContextKind> ArgumentKinds,
    bool LastArgumentRepeats = false
)
{
    /// <summary>
    /// The completion kind for the argument at <paramref name="position"/>, or
    /// <c>null</c> when that position takes no completion.
    /// </summary>
    public CompletionContextKind? ArgumentKindAt(int position)
    {
        if (position < 0)
        {
            return null;
        }
        if (position < ArgumentKinds.Length)
        {
            return ArgumentKinds[position];
        }
        if (LastArgumentRepeats && ArgumentKinds.Length > 0)
        {
            return ArgumentKinds[^1];
        }
        return null;
    }
}
