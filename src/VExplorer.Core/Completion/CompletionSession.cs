namespace VExplorer.Core.Completion;

/// <summary>
/// Immutable snapshot of an in-progress Tab cycle. Holds the candidate list,
/// the selected index and the token range being rewritten, and knows how to
/// render the resulting buffer + caret. All state for zsh-style cycling lives
/// here (the view model holds one of these), keeping the logic pure/testable.
/// </summary>
public sealed record CompletionSession(
    IReadOnlyList<CompletionCandidate> Candidates,
    int SelectedIndex,
    int TokenStart,
    string OriginalBuffer
)
{
    public bool IsEmpty => Candidates.Count == 0;

    /// <summary>The currently selected candidate, or <c>null</c> when empty.</summary>
    public CompletionCandidate? Selected => IsEmpty ? null : Candidates[SelectedIndex];

    /// <summary>Advance the selection by one, wrapping at the end. No-op when empty.</summary>
    public CompletionSession MoveNext()
    {
        return Move(+1);
    }

    /// <summary>Step the selection back by one, wrapping at the start. No-op when empty.</summary>
    public CompletionSession MovePrev()
    {
        return Move(-1);
    }

    /// <summary>Select a specific index (e.g. from a click). No-op when out of range.</summary>
    public CompletionSession Select(int index)
    {
        return IsEmpty || index < 0 || index >= Candidates.Count
            ? this
            : this with
            {
                SelectedIndex = index,
            };
    }

    private CompletionSession Move(int delta)
    {
        if (IsEmpty)
        {
            return this;
        }
        int count = Candidates.Count;
        int next = ((SelectedIndex + delta) % count + count) % count;
        return this with { SelectedIndex = next };
    }

    /// <summary>
    /// The buffer text after applying the selected candidate: everything before
    /// <see cref="TokenStart"/> is preserved and the token is replaced by the
    /// candidate's insertion text. Returns the original buffer when empty.
    /// </summary>
    public string RenderBuffer()
    {
        if (IsEmpty)
        {
            return OriginalBuffer;
        }
        return OriginalBuffer[..TokenStart] + Candidates[SelectedIndex].InsertionText;
    }

    /// <summary>The caret position after <see cref="RenderBuffer"/> (end of insertion).</summary>
    public int CaretIndex => RenderBuffer().Length;
}
