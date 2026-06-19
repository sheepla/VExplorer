namespace VExplorer.Core.FileSystem;

/// <summary>
/// A single menu entry extracted from a shell <c>IContextMenu</c>. UI-agnostic:
/// icons are returned as native handles (see <see cref="IShellMenuSession.TryGetItemIcon"/>)
/// and materialized in the App layer, keeping Core free of WPF — the same split as
/// <see cref="IShellInfoProvider"/>.
/// </summary>
public sealed record ShellMenuItem
{
    /// <summary>Display label with the <c>&amp;</c> accelerators removed. Empty for separators.</summary>
    public required string Text { get; init; }

    public bool IsSeparator { get; init; }

    /// <summary>Has a submenu; call <see cref="IShellMenuSession.ExpandSubmenu"/> for its children.</summary>
    public bool HasSubmenu { get; init; }

    /// <summary>Grayed/disabled in the shell menu.</summary>
    public bool IsDisabled { get; init; }

    /// <summary>
    /// Opaque token unique within the owning <see cref="IShellMenuSession"/>. Passed back to
    /// <see cref="IShellMenuSession.Invoke"/> / <see cref="IShellMenuSession.ExpandSubmenu"/>.
    /// Distinct from the shell command id (which the session maps internally).
    /// </summary>
    public int Id { get; init; }

    /// <summary>The label could not be read (e.g. an owner-drawn third-party item).</summary>
    public bool LabelMissing { get; init; }

    /// <summary>
    /// The item's menu bitmap as an <c>HBITMAP</c> handle (best-effort; 0 when none or
    /// owner-drawn). Carried in the data so the UI never calls back into the (apartment-
    /// affine) session. The handle belongs to the shell menu — copy it before the session
    /// is disposed; never delete it.
    /// </summary>
    public nint IconHandle { get; init; }
}

/// <summary>
/// One open shell context menu. Holds the live <c>IContextMenu</c> and the off-screen
/// <c>HMENU</c> until disposed; <see cref="Invoke"/> sends the chosen command back to the
/// shell. Submenus are expanded lazily.
/// </summary>
public interface IShellMenuSession : IDisposable
{
    /// <summary>The top-level entries, in order (separators included).</summary>
    IReadOnlyList<ShellMenuItem> TopLevelItems { get; }

    /// <summary>The children of a submenu item; empty when it has none / cannot expand.</summary>
    IReadOnlyList<ShellMenuItem> ExpandSubmenu(int itemId);

    /// <summary>Runs the item's shell command. Returns false for separators/submenu parents/failure.</summary>
    bool Invoke(int itemId);
}

/// <summary>
/// Opens shell context menus and exposes their items as an abstract model so the MENU-mode
/// ViewModel can drive <c>hjkl</c> navigation without the shell drawing anything.
/// Implemented in the Shell layer.
/// </summary>
public interface IShellContextMenu
{
    /// <summary>
    /// The context menu for <paramref name="paths"/> (already resolved:
    /// selection else cursor). Returns null when none can be resolved.
    /// </summary>
    IShellMenuSession? OpenForItems(IReadOnlyList<string> paths, nint ownerHwnd);

    /// <summary>The folder-background context menu for <paramref name="folderPath"/>.</summary>
    IShellMenuSession? OpenForFolderBackground(string folderPath, nint ownerHwnd);
}
