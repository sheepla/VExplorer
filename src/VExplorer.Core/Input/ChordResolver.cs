namespace VExplorer.Core.Input;

/// <summary>
/// Folds the raw key stream into <see cref="KeyChord"/>s, completing two-key Vim
/// chords (<c>yy</c>, <c>dd</c>). Stateful: the first chord key is held pending;
/// the next key either completes the chord or is emitted on its own (clearing the
/// pending key). The caller decides when chords are eligible (e.g. only on list
/// focus) and resets on mode/tab changes via <see cref="Reset"/>.
/// </summary>
public sealed class ChordResolver
{
    private static readonly AppKey[] ChordKeys = [AppKey.Y, AppKey.D];

    private AppKey? _pending;

    /// <summary>
    /// Resolves the next key. Returns null when the key is held pending the second
    /// chord key (the caller should take no action); otherwise returns the chord to
    /// look up — either a completed two-key chord or a plain single key.
    /// </summary>
    /// <param name="chordEligible">
    /// Whether chord folding applies right now (the app only chords on list focus
    /// with no modifiers); when false, keys pass through and any pending key clears.
    /// </param>
    public KeyChord? Resolve(AppKey key, AppModifiers modifiers, bool chordEligible)
    {
        AppKey? pending = _pending;
        _pending = null;

        bool isChordKey =
            chordEligible && modifiers == AppModifiers.None && Array.IndexOf(ChordKeys, key) >= 0;
        if (isChordKey)
        {
            if (pending == key)
            {
                return new KeyChord(key, key, AppModifiers.None);
            }
            _pending = key;
            return null;
        }

        return new KeyChord(key, modifiers);
    }

    /// <summary>Clears any pending chord key (call on mode/tab/focus changes).</summary>
    public void Reset()
    {
        _pending = null;
    }
}
