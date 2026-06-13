namespace VExplorer.Core.Completion;

/// <summary>
/// A single completion candidate.
/// </summary>
/// <param name="InsertionText">
/// The text that replaces the current token (from <c>TokenStart</c> to the
/// caret). For path completion this includes the directory portion, e.g. the
/// candidate "Users" under <c>C:\</c> has <c>InsertionText = "C:\Users\"</c>.
/// Folders carry a trailing separator; files do not.
/// </param>
/// <param name="DisplayName">The label shown in the popup (leaf name only).</param>
/// <param name="Kind">The candidate category (icon + accept behaviour).</param>
public sealed record CompletionCandidate(
    string InsertionText,
    string DisplayName,
    CompletionKind Kind
);
