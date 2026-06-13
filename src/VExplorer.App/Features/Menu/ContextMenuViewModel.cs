using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VExplorer.Core.FileSystem;

namespace VExplorer.App.Features.Menu;

/// <summary>
/// Drives the MENU-mode context menu as a horizontal cascade of <see cref="MenuColumnViewModel"/>:
/// the right-most column is active and receives navigation; entering a submenu adds a column,
/// going back removes one. Built-in items open instantly; the native "More options" submenu
/// loads its children from the worker-thread shell session (so the UI never blocks).
/// </summary>
public sealed partial class ContextMenuViewModel : ObservableObject
{
    /// <summary>Half-page step for Ctrl+D / Ctrl+U.</summary>
    private const int PageStep = 8;

    private HostedMenuSession? _session;
    private MenuColumnViewModel? _loadingMoreColumn;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Popup anchor relative to the file list (set at open time).</summary>
    [ObservableProperty]
    private double _anchorX;

    [ObservableProperty]
    private double _anchorY;

    public ObservableCollection<MenuColumnViewModel> Columns { get; } = [];

    /// <summary>
    /// Bumped on every open/close. The async shell load carries the generation it started under
    /// and is dropped if it no longer matches (the menu was closed/reopened meanwhile).
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>The active (right-most) column — the one the keys drive.</summary>
    private MenuColumnViewModel? ActiveColumn => Columns.Count > 0 ? Columns[^1] : null;

    /// <summary>The highlighted item in the active column.</summary>
    public MenuItemViewModel? SelectedItem => ActiveColumn?.SelectedItem;

    /// <summary>
    /// Opens instantly with the built-in items plus a trailing "More options" entry; the native
    /// shell items are loaded lazily under it via <see cref="AppendShellSession"/>.
    /// </summary>
    public void OpenSelfOnly(
        IReadOnlyList<MenuItemViewModel> selfItems,
        double anchorX,
        double anchorY
    )
    {
        Close();
        Generation++;
        AnchorX = anchorX;
        AnchorY = anchorY;

        List<MenuItemViewModel> root =
        [
            .. selfItems,
            MenuItemViewModel.Separator,
            MoreOptionsEntry(),
        ];
        IReadOnlyList<MenuItemViewModel> items = Normalize(root);
        Columns.Add(new MenuColumnViewModel(items, NextSelectable(items, -1, +1)));
        IsOpen = true;
    }

    /// <summary>Stores the loaded shell session; refreshes the "More options" column if it is waiting.</summary>
    public void AppendShellSession(HostedMenuSession? session, int generation)
    {
        if (generation != Generation || !IsOpen)
        {
            session?.Dispose();
            return;
        }
        _session = session;
        if (_loadingMoreColumn is not null && ReferenceEquals(ActiveColumn, _loadingMoreColumn))
        {
            ReplaceActiveColumn(BuildShellTopLevel());
            _loadingMoreColumn = null;
        }
    }

    public void MoveDown() => Step(+1);

    public void MoveUp() => Step(-1);

    public void PageDown() => Step(+PageStep);

    public void PageUp() => Step(-PageStep);

    public void MoveToFirst()
    {
        if (ActiveColumn is { } col)
        {
            col.SelectedIndex = NextSelectable(col.Items, -1, +1);
        }
    }

    public void MoveToLast()
    {
        if (ActiveColumn is { } col)
        {
            col.SelectedIndex = NextSelectable(col.Items, col.Items.Count, -1);
        }
    }

    private void Step(int delta)
    {
        if (ActiveColumn is not { } col)
        {
            return;
        }
        int dir = delta >= 0 ? +1 : -1;
        int idx = col.SelectedIndex;
        for (int n = 0; n < Math.Abs(delta); n++)
        {
            int next = NextSelectable(col.Items, idx, dir);
            if (next == idx)
            {
                break;
            }
            idx = next;
        }
        col.SelectedIndex = idx;
    }

    /// <summary>Open the highlighted item's submenu as a new column to the right (the <c>l</c> key).</summary>
    public async Task EnterSubmenuAsync()
    {
        if (SelectedItem is not { HasSubmenu: true } cur)
        {
            return;
        }
        if (cur.SelfSubItems is { } sub)
        {
            PushColumn(Normalize(sub));
            return;
        }
        if (cur.IsShellRoot)
        {
            if (_session is null)
            {
                PushColumn([Disabled("Loading…")]);
                _loadingMoreColumn = ActiveColumn;
                return;
            }
            PushColumn(BuildShellTopLevel());
            return;
        }
        if (cur.IsShell && _session is { } session)
        {
            int gen = Generation;
            IReadOnlyList<ShellMenuItem> kids = await session.ExpandSubmenuAsync(cur.ShellId);
            if (gen != Generation || !IsOpen)
            {
                return; // closed/reopened during the await
            }
            IReadOnlyList<MenuItemViewModel> children = Normalize(BuildShellItems(kids));
            if (children.Count > 0)
            {
                PushColumn(children);
            }
        }
    }

    /// <summary>Close the right-most column (the <c>h</c> key); keeps at least the root.</summary>
    public void Back()
    {
        if (Columns.Count <= 1)
        {
            return;
        }
        if (ReferenceEquals(Columns[^1], _loadingMoreColumn))
        {
            _loadingMoreColumn = null;
        }
        Columns.RemoveAt(Columns.Count - 1);
    }

    /// <summary>Runs a shell item's command on the worker. Self items are run by the caller.</summary>
    public async Task InvokeShellAsync(MenuItemViewModel item)
    {
        if (item.IsShell && _session is { } session)
        {
            await session.InvokeAsync(item.ShellId);
        }
    }

    public void Close()
    {
        IsOpen = false;
        Columns.Clear();
        _loadingMoreColumn = null;
        _session?.Dispose();
        _session = null;
    }

    private void PushColumn(IReadOnlyList<MenuItemViewModel> items)
    {
        Columns.Add(new MenuColumnViewModel(items, NextSelectable(items, -1, +1)));
    }

    private void ReplaceActiveColumn(IReadOnlyList<MenuItemViewModel> items)
    {
        Columns[^1] = new MenuColumnViewModel(items, NextSelectable(items, -1, +1));
    }

    private IReadOnlyList<MenuItemViewModel> BuildShellTopLevel()
    {
        IReadOnlyList<MenuItemViewModel> items = _session is { } s
            ? Normalize(BuildShellItems(s.TopLevelItems))
            : [];
        return items.Count > 0 ? items : [Disabled("(no items)")];
    }

    private static MenuItemViewModel MoreOptionsEntry() =>
        new() { Text = "More options", IsShellRoot = true };

    private static MenuItemViewModel Disabled(string text) =>
        new() { Text = text, IsEnabled = false };

    private static IReadOnlyList<MenuItemViewModel> BuildShellItems(
        IReadOnlyList<ShellMenuItem> items
    )
    {
        List<MenuItemViewModel> result = new(items.Count);
        foreach (ShellMenuItem si in items)
        {
            if (si.IsSeparator)
            {
                result.Add(MenuItemViewModel.Separator);
                continue;
            }
            result.Add(
                new MenuItemViewModel
                {
                    Text = si.LabelMissing ? "(unnamed)" : si.Text,
                    IsEnabled = !si.IsDisabled,
                    ShellHasSubmenu = si.HasSubmenu,
                    ShellId = si.Id,
                    Icon = ToImage(si.IconHandle),
                }
            );
        }
        return result;
    }

    /// <summary>Drops leading/trailing separators and collapses consecutive ones into clean sections.</summary>
    private static IReadOnlyList<MenuItemViewModel> Normalize(
        IReadOnlyList<MenuItemViewModel> items
    )
    {
        List<MenuItemViewModel> result = [];
        foreach (MenuItemViewModel item in items)
        {
            if (item.IsSeparator && (result.Count == 0 || result[^1].IsSeparator))
            {
                continue; // no leading / no doubled separators
            }
            result.Add(item);
        }
        if (result.Count > 0 && result[^1].IsSeparator)
        {
            result.RemoveAt(result.Count - 1); // no trailing separator
        }
        return result;
    }

    /// <summary>Index of the next selectable item from <paramref name="from"/> in <paramref name="dir"/>; the current one if none.</summary>
    private static int NextSelectable(IReadOnlyList<MenuItemViewModel> items, int from, int dir)
    {
        for (int i = from + dir; i >= 0 && i < items.Count; i += dir)
        {
            if (items[i].IsSelectable)
            {
                return i;
            }
        }
        return from >= 0 && from < items.Count && items[from].IsSelectable
            ? from
            : FirstSelectable(items);
    }

    private static int FirstSelectable(IReadOnlyList<MenuItemViewModel> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].IsSelectable)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Materializes a menu HBITMAP into a frozen WPF image (best-effort; null on failure).</summary>
    private static ImageSource? ToImage(nint hbitmap)
    {
        if (hbitmap == 0)
        {
            return null;
        }
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap,
                0,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}
