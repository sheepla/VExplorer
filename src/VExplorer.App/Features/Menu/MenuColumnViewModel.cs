using CommunityToolkit.Mvvm.ComponentModel;

namespace VExplorer.App.Features.Menu;

/// <summary>
/// One column of the cascading context menu: a level's items and its cursor. The active
/// (right-most) column receives keyboard navigation; earlier columns keep their selected
/// parent highlighted so the open path stays visible.
/// </summary>
public sealed partial class MenuColumnViewModel(
    IReadOnlyList<MenuItemViewModel> items,
    int selectedIndex
) : ObservableObject
{
    public IReadOnlyList<MenuItemViewModel> Items { get; } = items;

    [ObservableProperty]
    private int _selectedIndex = selectedIndex;

    public MenuItemViewModel? SelectedItem =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
}
