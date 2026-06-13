using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using R3;
using VExplorer.Core.State;

namespace VExplorer.App.Features.Tabs;

/// <summary>
/// One tab in the tab strip. Exposes the live folder label (from the tab's
/// <see cref="TabState.CurrentLocation"/>), an active flag for highlighting, and
/// activate / close commands routed back to the owning <see cref="TabBarViewModel"/>.
/// The underlying <see cref="TabState"/> is owned by the tab's DI scope and is
/// not disposed here — only the location subscription is.
/// </summary>
public sealed partial class TabItemViewModel : ObservableObject, IDisposable
{
    private readonly IDisposable _titleSubscription;
    private readonly IDisposable _loadingSubscription;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private bool _isActive;

    /// <summary>True while this tab is (re-)listing — drives the tab's loading spinner.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>1-based position, shown as "[1]" and matching the digit-key shortcut.</summary>
    [ObservableProperty]
    private int _number;

    public Guid Id { get; }

    public ICommand ActivateCommand { get; }
    public ICommand CloseCommand { get; }

    public TabItemViewModel(
        Guid id,
        int number,
        TabState tabState,
        Action<Guid> activate,
        Action<Guid> close
    )
    {
        Id = id;
        Number = number;
        ActivateCommand = new RelayCommand(() => activate(id));
        CloseCommand = new RelayCommand(() => close(id));
        _titleSubscription = tabState
            .CurrentLocation.ObserveOnCurrentDispatcher()
            .Subscribe(loc => Title = LocationLabels.Folder(loc));
        _loadingSubscription = tabState
            .IsLoading.ObserveOnCurrentDispatcher()
            .Subscribe(loading => IsLoading = loading);
    }

    public void Dispose()
    {
        _titleSubscription.Dispose();
        _loadingSubscription.Dispose();
    }
}
