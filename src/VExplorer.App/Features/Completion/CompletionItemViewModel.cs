using VExplorer.Core.Completion;

namespace VExplorer.App.Features.Completion;

/// <summary>
/// A single row in a completion popup: an icon glyph + the leaf name only.
/// Sizes and timestamps are intentionally omitted (only kind + name are needed
/// to choose where to descend). Shared by the address bar and command bar.
/// </summary>
public sealed class CompletionItemViewModel(CompletionCandidate candidate)
{
    public CompletionCandidate Candidate { get; } = candidate;

    public string DisplayName => Candidate.DisplayName;

    public string Icon =>
        Candidate.Kind switch
        {
            CompletionKind.Folder => "📁",
            CompletionKind.SpecialFolder => "🔗",
            CompletionKind.Command => "⌘",
            _ => "📄",
        };
}
