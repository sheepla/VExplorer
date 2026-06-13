using CommunityToolkit.Mvvm.ComponentModel;
using R3;
using VExplorer.App.Features.AddressBar;
using VExplorer.App.Features.CommandBar;
using VExplorer.App.Features.FileList;
using VExplorer.App.Features.StatusBar;
using VExplorer.App.Features.Tabs;
using VExplorer.App.Features.Tree;
using VExplorer.Core.State;

namespace VExplorer.App;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly TabManager _tabManager;
    private readonly IDisposable _activeTabSubscription;

    // Focus/Title/IsLoading track the active tab and are re-pointed on every tab switch.
    private IDisposable? _focusSubscription;
    private IDisposable? _titleSubscription;
    private IDisposable? _loadingSubscription;

    /// <summary>True while the active tab is (re-)listing — drives the thin progress bar.</summary>
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isTreeFocused;

    [ObservableProperty]
    private bool _isListFocused = true;

    /// <summary>Window title: "&lt;current folder&gt; - VExplorer".</summary>
    [ObservableProperty]
    private string _title = "VExplorer";

    public TabBarViewModel TabBar { get; }

    /// <summary>App-global MENU-mode context menu (one open at a time).</summary>
    public Features.Menu.ContextMenuViewModel ContextMenu { get; } = new();

    public MainWindowViewModel(TabManager tabManager, AppState appState, TabBarViewModel tabBar)
    {
        _tabManager = tabManager;
        TabBar = tabBar;

        // Re-point per-tab subscriptions and refresh all proxied view models
        // whenever the active tab changes (covers keys, clicks, close-reselect).
        _activeTabSubscription = appState
            .ActiveTabId.ObserveOnCurrentDispatcher()
            .Subscribe(_ => RebindActiveTab());
    }

    private void RebindActiveTab()
    {
        _focusSubscription?.Dispose();
        _titleSubscription?.Dispose();
        _loadingSubscription?.Dispose();

        TabState tab = _tabManager.GetActiveTabState();
        _focusSubscription = tab
            .Focus.ObserveOnCurrentDispatcher()
            .Subscribe(f =>
            {
                IsTreeFocused = f == Core.Modes.Focus.Tree;
                IsListFocused = f == Core.Modes.Focus.List;
            });
        _titleSubscription = tab
            .CurrentLocation.ObserveOnCurrentDispatcher()
            .Subscribe(loc => Title = $"{LocationLabels.Folder(loc)} - VExplorer");
        _loadingSubscription = tab
            .IsLoading.ObserveOnCurrentDispatcher()
            .Subscribe(loading => IsLoading = loading);

        // Force every proxy binding to refetch from the new tab's scope.
        OnPropertyChanged(nameof(FileList));
        OnPropertyChanged(nameof(Tree));
        OnPropertyChanged(nameof(StatusBar));
        OnPropertyChanged(nameof(AddressBar));
        OnPropertyChanged(nameof(CommandBar));
        OnPropertyChanged(nameof(SearchFilterBar));
    }

    public FileListViewModel FileList => _tabManager.GetActiveScopedService<FileListViewModel>();

    public TreeViewModel Tree => _tabManager.GetActiveScopedService<TreeViewModel>();

    public StatusBarViewModel StatusBar => _tabManager.GetActiveScopedService<StatusBarViewModel>();

    public AddressBarViewModel AddressBar =>
        _tabManager.GetActiveScopedService<AddressBarViewModel>();

    public CommandBarViewModel CommandBar =>
        _tabManager.GetActiveScopedService<CommandBarViewModel>();

    public Features.Search.SearchFilterBarViewModel SearchFilterBar =>
        _tabManager.GetActiveScopedService<Features.Search.SearchFilterBarViewModel>();

    public void Dispose()
    {
        _activeTabSubscription.Dispose();
        _focusSubscription?.Dispose();
        _titleSubscription?.Dispose();
        _loadingSubscription?.Dispose();
        TabBar.Dispose();
    }
}
