using CommunityToolkit.Mvvm.ComponentModel;
using R3;
using VExplorer.Core.Modes;
using VExplorer.Core.State;

namespace VExplorer.App.Features.Search;

/// <summary>
/// Drives the one-line SEARCH (<c>/</c>) and FILTER (<c>F</c>) input bar. Unlike
/// the address/command bars it has no completion — each keystroke pushes the live
/// query to central state via <see cref="ModeEvent.UpdateQuery"/>; the file list
/// reacts (incremental jump for search, narrowing for filter). Enter confirms,
/// Esc cancels.
/// </summary>
public sealed partial class SearchFilterBarViewModel : ObservableObject, IDisposable
{
    private readonly IDisposable _modeSubscription;
    private bool _suppress;
    private bool _wasActive;

    /// <summary>The active bar kind (Search/Filter), or null while inactive — detects a Search↔Filter switch.</summary>
    private Mode? _activeKind;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private string _prefix = "SEARCH";

    public TabState TabState { get; }

    public SearchFilterBarViewModel(TabState tabState)
    {
        TabState = tabState;
        _modeSubscription = tabState.Mode.ObserveOnCurrentDispatcher().Subscribe(OnModeChanged);
    }

    private void OnModeChanged(Mode mode)
    {
        Prefix = mode is Mode.Filter ? "FILTER" : "SEARCH";
        bool active = mode is Mode.Search or Mode.Filter;
        // A Search↔Filter switch keeps the bar active but is a fresh input.
        bool kindChanged = active && _activeKind?.GetType() != mode.GetType();
        if (active == _wasActive && !kindChanged)
        {
            return;
        }
        _wasActive = active;
        _activeKind = active ? mode : null;
        IsActive = active;
        if (active)
        {
            SetTextSilently("");
        }
    }

    partial void OnTextChanged(string value)
    {
        if (_suppress || !IsActive)
        {
            return;
        }
        TabState.DispatchModeEvent(new ModeEvent.UpdateQuery(value));
    }

    /// <summary>Enter: confirm the query (search keeps matches for n/N; filter persists).</summary>
    public void Confirm()
    {
        TabState.DispatchModeEvent(new ModeEvent.ConfirmMode());
        TabState.DispatchModeEvent(new ModeEvent.ExitToNormal());
    }

    /// <summary>Esc: cancel (search restores the cursor; filter is dropped).</summary>
    public void Cancel()
    {
        TabState.DispatchModeEvent(new ModeEvent.ExitToNormal());
    }

    private void SetTextSilently(string value)
    {
        _suppress = true;
        Text = value;
        _suppress = false;
    }

    public void Dispose()
    {
        _modeSubscription.Dispose();
    }
}
