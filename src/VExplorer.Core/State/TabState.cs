using System.Collections.Immutable;
using R3;
using VExplorer.Core.FileSystem;
using VExplorer.Core.Modes;

namespace VExplorer.Core.State;

public sealed class TabState : IDisposable
{
    private readonly ReactiveProperty<Location> _currentLocation;

    /// <summary>Browser-style visited-location history (back/forward) for this tab.</summary>
    private readonly List<Location> _history = [];
    private int _historyIndex = -1;

    private readonly ReactiveProperty<Mode> _mode;
    private readonly ReactiveProperty<Modes.Focus> _focus;
    private readonly ReactiveProperty<ImmutableHashSet<int>> _selection;
    private readonly ReactiveProperty<int> _cursorIndex;
    private readonly ReactiveProperty<StatusMessage> _statusMessage;

    /// <summary>True while this tab is (re-)listing its current location.</summary>
    private readonly ReactiveProperty<bool> _isLoading;

    /// <summary>Fires when the current location should be re-listed in place.</summary>
    private readonly Subject<Unit> _refreshRequested = new();

    /// <summary>Name to focus after the next (re)load, e.g. a just-operated file.</summary>
    private string? _pendingFocusName;

    public TabState()
    {
        _currentLocation = new ReactiveProperty<Location>(KnownLocations.Pc);
        // Seed history with the initial location so back/forward and the address
        // bar's history candidates have a starting point (OpenTab's NavigateTo to
        // the same location is a no-op and would otherwise leave history empty).
        _history.Add(KnownLocations.Pc);
        _historyIndex = 0;
        _mode = new ReactiveProperty<Mode>(new Mode.Normal());
        _focus = new ReactiveProperty<Modes.Focus>(Modes.Focus.List);
        _selection = new ReactiveProperty<ImmutableHashSet<int>>(ImmutableHashSet<int>.Empty);
        _cursorIndex = new ReactiveProperty<int>(0);
        _statusMessage = new ReactiveProperty<StatusMessage>(new StatusMessage("", false));
        _isLoading = new ReactiveProperty<bool>(false);
    }

    public Observable<Location> CurrentLocation => _currentLocation;
    public Observable<Mode> Mode => _mode;
    public Observable<Modes.Focus> Focus => _focus;
    public Observable<ImmutableHashSet<int>> Selection => _selection;
    public Observable<int> CursorIndex => _cursorIndex;
    public Observable<StatusMessage> StatusMessage => _statusMessage;
    public Observable<bool> IsLoading => _isLoading;
    public Observable<Unit> RefreshRequested => _refreshRequested;

    public Location CurrentLocationValue => _currentLocation.Value;

    /// <summary>The current location's filesystem path, or <c>null</c> for shell-only locations.</summary>
    public string? CurrentDirectoryPath =>
        _currentLocation.Value.TryGetFileSystemPath(out string p) ? p : null;

    public Mode ModeValue => _mode.Value;
    public Modes.Focus FocusValue => _focus.Value;
    public ImmutableHashSet<int> SelectionValue => _selection.Value;
    public int CursorIndexValue => _cursorIndex.Value;
    public StatusMessage StatusMessageValue => _statusMessage.Value;
    public bool IsLoadingValue => _isLoading.Value;

    /// <summary>Marks this tab as loading (true) or idle (false) for the spinner / progress bar.</summary>
    public void SetLoading(bool value)
    {
        _isLoading.Value = value;
    }

    public void NavigateTo(Location location)
    {
        if (_currentLocation.Value.Equals(location))
        {
            return;
        }
        // A fresh navigation truncates any forward history, then appends.
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }
        _history.Add(location);
        _historyIndex = _history.Count - 1;
        _currentLocation.Value = location;
    }

    /// <summary>Convenience for navigating to a filesystem path.</summary>
    public void NavigateTo(string path)
    {
        NavigateTo(Location.ForPath(path));
    }

    // ── Navigation history (back / forward) ────────────────────────────────

    /// <summary>The visited locations, oldest first (for address-bar history candidates).</summary>
    public IReadOnlyList<Location> History => _history;

    public bool CanGoBack => _historyIndex > 0;
    public bool CanGoForward => _historyIndex < _history.Count - 1;

    /// <summary>Move one step back in history without altering it. No-op at the start.</summary>
    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }
        _historyIndex--;
        _currentLocation.Value = _history[_historyIndex];
    }

    /// <summary>Move one step forward in history without altering it. No-op at the end.</summary>
    public void GoForward()
    {
        if (!CanGoForward)
        {
            return;
        }
        _historyIndex++;
        _currentLocation.Value = _history[_historyIndex];
    }

    public void DispatchModeEvent(ModeEvent @event)
    {
        if (@event is ModeEvent.EnterVisual && _focus.Value != Modes.Focus.List)
        {
            throw new InvalidOperationException("VISUAL mode requires LIST focus.");
        }
        _mode.Value = ModeMachine.Transition(_mode.Value, @event);
    }

    public void SetFocus(Modes.Focus focus)
    {
        _focus.Value = focus;
    }

    public void SetSelection(ImmutableHashSet<int> selection)
    {
        _selection.Value = selection;
    }

    public void SetCursorIndex(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Cursor index must be non-negative."
            );
        }
        _cursorIndex.Value = index;
    }

    public void SetStatusMessage(string message, bool isError = true)
    {
        _statusMessage.Value = new StatusMessage(message, isError);
    }

    /// <summary>
    /// Clears a lingering error message so it does not outlive the action that
    /// produced it. Called when the user performs any new operation. No-op (and
    /// no notification) when nothing is currently shown.
    /// </summary>
    public void ClearErrorMessage()
    {
        if (_statusMessage.Value.IsError)
        {
            _statusMessage.Value = new StatusMessage("", false);
        }
    }

    /// <summary>
    /// Requests a re-listing of the current directory (e.g. after a file op).
    /// <paramref name="focusName"/>, when given, becomes the cursor target after
    /// the reload (used to focus a just-created/renamed/pasted item).
    /// </summary>
    public void RequestRefresh(string? focusName = null)
    {
        if (focusName != null)
        {
            _pendingFocusName = focusName;
        }
        _refreshRequested.OnNext(Unit.Default);
    }

    /// <summary>
    /// Sets the focus target for the next (re)load without firing a refresh — used
    /// to focus a file on initial load (e.g. a command-line path argument).
    /// </summary>
    public void SetPendingFocusName(string name)
    {
        _pendingFocusName = name;
    }

    /// <summary>Returns and clears the pending focus name set by <see cref="RequestRefresh"/>.</summary>
    public string? ConsumePendingFocusName()
    {
        string? name = _pendingFocusName;
        _pendingFocusName = null;
        return name;
    }

    public void Dispose()
    {
        _currentLocation.Dispose();
        _mode.Dispose();
        _focus.Dispose();
        _selection.Dispose();
        _cursorIndex.Dispose();
        _statusMessage.Dispose();
        _isLoading.Dispose();
        _refreshRequested.Dispose();
    }
}
