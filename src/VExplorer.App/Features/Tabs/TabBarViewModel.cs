using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using R3;
using VExplorer.App.Themes;
using VExplorer.Core.State;

namespace VExplorer.App.Features.Tabs;

/// <summary>
/// Backs the top tab strip. Rebuilds its <see cref="Tabs"/> collection whenever
/// the set of open tabs changes and tracks the active tab for highlighting.
/// Tab activation goes straight through <see cref="TabManager"/>; closing is
/// surfaced via <see cref="CloseRequested"/> so the window can apply the
/// "close last tab → exit" policy in one place.
/// </summary>
public sealed partial class TabBarViewModel : ObservableObject, IDisposable
{
    // Segoe MDL2 Assets glyphs: sun (switch to light) while dark, moon while light.
    private const string SunGlyph = "";
    private const string MoonGlyph = "";

    private readonly TabManager _tabManager;
    private readonly AppState _appState;
    private readonly IDisposable _tabsSubscription;
    private readonly IDisposable _activeSubscription;
    private readonly IDisposable _themeSubscription;
    private readonly Subject<Guid> _closeRequested = new();

    public ObservableCollection<TabItemViewModel> Tabs { get; } = [];

    public ICommand NewTabCommand { get; }

    /// <summary>Toggles between light and dark; the icon reflects the next state.</summary>
    public ICommand ToggleThemeCommand { get; }

    /// <summary>Segoe MDL2 glyph shown on the dark-mode toggle button.</summary>
    [ObservableProperty]
    private string _themeIcon = MoonGlyph;

    /// <summary>Tooltip for the dark-mode toggle button (reflects the next state).</summary>
    [ObservableProperty]
    private string _themeToggleTooltip = "Switch to dark theme";

    /// <summary>Fires the id of a tab the user asked to close (× button).</summary>
    public Observable<Guid> CloseRequested => _closeRequested;

    public TabBarViewModel(TabManager tabManager, AppState appState, ThemeManager themeManager)
    {
        _tabManager = tabManager;
        _appState = appState;
        NewTabCommand = new RelayCommand(() => _tabManager.OpenTab());
        ToggleThemeCommand = new RelayCommand(themeManager.Toggle);

        _tabsSubscription = tabManager
            .TabsChanged.ObserveOnCurrentDispatcher()
            .Subscribe(_ => Rebuild());
        _activeSubscription = appState
            .ActiveTabId.ObserveOnCurrentDispatcher()
            .Subscribe(_ => ApplyActive());
        _themeSubscription = themeManager
            .IsDarkChanged.ObserveOnCurrentDispatcher()
            .Subscribe(dark =>
            {
                ThemeIcon = dark ? SunGlyph : MoonGlyph;
                ThemeToggleTooltip = dark ? "Switch to light theme" : "Switch to dark theme";
            });
        Rebuild();
    }

    /// <summary>Drag &amp; drop reorder: move the source tab to the target tab's slot.</summary>
    public void MoveTab(Guid source, Guid target)
    {
        if (source == target)
        {
            return;
        }
        int targetIndex = _tabManager.TabOrder.ToList().IndexOf(target);
        if (targetIndex >= 0)
        {
            _tabManager.MoveTab(source, targetIndex);
        }
    }

    private void Rebuild()
    {
        foreach (TabItemViewModel tab in Tabs)
        {
            tab.Dispose();
        }
        Tabs.Clear();

        IReadOnlyList<Guid> order = _tabManager.TabOrder;
        for (int i = 0; i < order.Count; i++)
        {
            Guid id = order[i];
            Tabs.Add(
                new TabItemViewModel(
                    id,
                    number: i + 1,
                    _tabManager.GetTabState(id),
                    activate: _appState.SetActiveTab,
                    close: id => _closeRequested.OnNext(id)
                )
            );
        }
        ApplyActive();
    }

    private void ApplyActive()
    {
        Guid active = _appState.ActiveTabIdValue;
        foreach (TabItemViewModel tab in Tabs)
        {
            tab.IsActive = tab.Id == active;
        }
    }

    public void Dispose()
    {
        _tabsSubscription.Dispose();
        _activeSubscription.Dispose();
        _themeSubscription.Dispose();
        foreach (TabItemViewModel tab in Tabs)
        {
            tab.Dispose();
        }
        _closeRequested.Dispose();
    }
}
