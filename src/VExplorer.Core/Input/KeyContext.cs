using VExplorer.Core.Modes;

namespace VExplorer.Core.Input;

/// <summary>
/// The state a key binding may depend on: the current mode, which pane has focus,
/// and whether the current location is the Recycle Bin (where delete is permanent
/// and restore is available). Passed to <see cref="IKeyBindingSource.Resolve"/>.
/// </summary>
public readonly record struct KeyContext(ModeKind Mode, Focus Focus, bool IsRecycleBin);
