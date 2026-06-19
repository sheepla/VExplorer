using VExplorer.Core.Actions;

namespace VExplorer.Core.Input;

/// <summary>
/// Resolves input into an <see cref="AppAction"/>. The default implementation is a
/// hard-coded table (<see cref="KeyBindingMap"/>); the interface is the seam a
/// future config-file (e.g. TOML) source would slot into.
/// </summary>
public interface IKeyBindingSource
{
    /// <summary>The action for a physical key chord in the given context, or null when unbound.</summary>
    AppAction? Resolve(KeyContext context, KeyChord chord);

    /// <summary>
    /// The action for a typed character (layout-independent input such as
    /// <c>:</c>, <c>/</c>, <c>[</c>), or null when the character is not bound.
    /// </summary>
    AppAction? ResolveText(KeyContext context, string text);
}
