using System.Collections.Immutable;

namespace VExplorer.Core.Completion;

/// <summary>
/// Identifies which candidate source applies at the caret.
/// </summary>
public enum CompletionContextKind
{
    Path,
    SpecialFolder,
    CommandName,

    /// <summary>Previously-visited locations from the tab's navigation history.</summary>
    NavigationHistory,

    /// <summary>The current item's name (e.g. pre-fill for <c>:rename</c>).</summary>
    CurrentName,

    // Reserved for COMMAND-mode completion (not produced yet).
    SetOption,
    ExternalCommand,
}

/// <summary>
/// The classified completion context at a caret position: which token is being
/// completed, where it starts, and which providers (in order) should serve it.
/// </summary>
/// <param name="Buffer">The full input buffer.</param>
/// <param name="CaretIndex">The caret position within the buffer.</param>
/// <param name="Token">The substring being completed (from <paramref name="TokenStart"/> to the caret).</param>
/// <param name="TokenStart">The buffer index where the token begins; replacements rewrite from here.</param>
/// <param name="CurrentDirectory">The tab's current directory, used to resolve relative paths.</param>
/// <param name="Providers">The provider kinds to consult, in merge order.</param>
/// <param name="CommandName">
/// The command being completed, when in a COMMAND context. Reserved for future
/// command-argument completion; <c>null</c> in the ADDRESS context.
/// </param>
/// <param name="ArgumentIndex">
/// The 0-based argument position being completed in a COMMAND context. Reserved
/// for future use; <c>null</c> in the ADDRESS context.
/// </param>
public sealed record CompletionContext(
    string Buffer,
    int CaretIndex,
    string Token,
    int TokenStart,
    string CurrentDirectory,
    ImmutableArray<CompletionContextKind> Providers,
    string? CommandName = null,
    int? ArgumentIndex = null
);
