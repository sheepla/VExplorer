using System.Windows.Media;

namespace VExplorer.App.Features.Menu;

/// <summary>
/// One row in the MENU-mode context menu. A leaf is either a self item (runs
/// <see cref="SelfAction"/>) or a shell item (executed via the menu session by
/// <see cref="ShellId"/>). A submenu carries built-in children (<see cref="SelfSubItems"/>),
/// is the native "More options" root (<see cref="IsShellRoot"/>), or is a shell submenu
/// (<see cref="ShellId"/> + <see cref="HasSubmenu"/>). Separators carry no action.
/// </summary>
public sealed class MenuItemViewModel
{
    public required string Text { get; init; }
    public bool IsSeparator { get; init; }
    public bool IsEnabled { get; init; } = true;
    public ImageSource? Icon { get; init; }

    /// <summary>Self-item handler (null for shell items / submenus / separators).</summary>
    public Action? SelfAction { get; init; }

    /// <summary>Shell session token, or -1 when this is not a shell item.</summary>
    public int ShellId { get; init; } = -1;

    /// <summary>Built-in submenu children (e.g. Pin ▸), or null.</summary>
    public IReadOnlyList<MenuItemViewModel>? SelfSubItems { get; init; }

    /// <summary>The "More options" entry whose children are the shell session's top-level items.</summary>
    public bool IsShellRoot { get; init; }

    /// <summary>True when this is a shell leaf/submenu item (vs a built-in self item).</summary>
    public bool IsShell => ShellId >= 0;

    /// <summary>True for shell submenu items (set by the shell extraction).</summary>
    public bool ShellHasSubmenu { get; init; }

    /// <summary>Has a child level (built-in submenu, More options, or a shell submenu).</summary>
    public bool HasSubmenu => SelfSubItems is not null || IsShellRoot || ShellHasSubmenu;

    /// <summary>Selectable by the cursor (not a separator, and enabled).</summary>
    public bool IsSelectable => !IsSeparator && IsEnabled;

    public static MenuItemViewModel Separator { get; } =
        new()
        {
            Text = "",
            IsSeparator = true,
            IsEnabled = false,
        };
}
