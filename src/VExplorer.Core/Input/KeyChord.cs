namespace VExplorer.Core.Input;

/// <summary>
/// A resolved key press: a single key with modifiers, or a two-key Vim chord
/// (e.g. <c>yy</c>, <c>dd</c>) where <see cref="Second"/> is set. The
/// <c>ChordResolver</c> produces these from the raw key stream.
/// </summary>
public readonly record struct KeyChord(AppKey First, AppKey? Second, AppModifiers Modifiers)
{
    public KeyChord(AppKey first, AppModifiers modifiers = AppModifiers.None)
        : this(first, null, modifiers) { }
}
