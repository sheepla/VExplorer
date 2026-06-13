using Microsoft.Extensions.DependencyInjection;
using R3;
using VExplorer.Core.FileSystem;
using VExplorer.Core.State;

namespace VExplorer.App;

public sealed class TabManager(IServiceProvider rootProvider, AppState appState) : IDisposable
{
    private sealed record TabEntry(Guid Id, IServiceScope Scope);

    private readonly IServiceProvider _rootProvider = rootProvider;
    private readonly AppState _appState = appState;

    // Ordered list is the source of truth for tab order; the dictionary is an
    // O(1) lookup by id. Both are kept in sync on every structural change.
    private readonly List<TabEntry> _tabs = [];
    private readonly Dictionary<Guid, TabEntry> _byId = [];
    private readonly Subject<Unit> _tabsChanged = new();

    /// <summary>Fires after any structural change (open / close / reorder).</summary>
    public Observable<Unit> TabsChanged => _tabsChanged;

    /// <summary>The open tab ids in display order.</summary>
    public IReadOnlyList<Guid> TabOrder => _tabs.Select(t => t.Id).ToList();

    /// <summary>
    /// Opens a new tab at <paramref name="initialLocation"/>, inserts it just
    /// after the active tab (or at the end when there is no active tab, e.g. on
    /// startup), makes it active, and notifies listeners.
    /// </summary>
    public Guid OpenTab(Location initialLocation)
    {
        IServiceScope scope = _rootProvider.CreateScope();
        TabState tabState = scope.ServiceProvider.GetRequiredService<TabState>();
        tabState.NavigateTo(initialLocation);

        Guid tabId = Guid.NewGuid();
        TabEntry entry = new(tabId, scope);

        int activeIndex = IndexOf(_appState.ActiveTabIdValue);
        int insertAt = activeIndex >= 0 ? activeIndex + 1 : _tabs.Count;
        _tabs.Insert(insertAt, entry);
        _byId[tabId] = entry;

        _appState.SetActiveTab(tabId);
        _tabsChanged.OnNext(Unit.Default);
        return tabId;
    }

    /// <summary>
    /// Opens a new tab at the active tab's current location (so cross-tab paste
    /// works immediately), falling back to the PC root when there is no active tab.
    /// </summary>
    public Guid OpenTab()
    {
        Location loc = _byId.TryGetValue(_appState.ActiveTabIdValue, out TabEntry? entry)
            ? entry.Scope.ServiceProvider.GetRequiredService<TabState>().CurrentLocationValue
            : KnownLocations.Pc;
        return OpenTab(loc);
    }

    public TabState GetActiveTabState()
    {
        return GetTabState(_appState.ActiveTabIdValue);
    }

    public TabState GetTabState(Guid tabId)
    {
        if (!_byId.TryGetValue(tabId, out TabEntry? entry))
        {
            throw new KeyNotFoundException($"No tab found with id {tabId}.");
        }
        return entry.Scope.ServiceProvider.GetRequiredService<TabState>();
    }

    /// <summary>
    /// Requests a re-listing of every open tab. Used after a live setting change
    /// (<c>:set</c>) so display options like hidden/system/folders-first apply at once.
    /// </summary>
    public void RefreshAllTabs()
    {
        foreach (TabEntry entry in _tabs)
        {
            entry.Scope.ServiceProvider.GetRequiredService<TabState>().RequestRefresh();
        }
    }

    public T GetActiveScopedService<T>()
        where T : notnull
    {
        if (!_byId.TryGetValue(_appState.ActiveTabIdValue, out TabEntry? entry))
        {
            throw new InvalidOperationException("No active tab.");
        }
        return entry.Scope.ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Closes a tab. Returns <c>true</c> when tabs remain afterwards, <c>false</c>
    /// when the last tab was closed (the caller decides whether to exit). Closing
    /// the active tab reselects its left neighbour (or the next when it was first);
    /// closing a background tab leaves the active tab unchanged.
    /// </summary>
    public bool CloseTab(Guid tabId)
    {
        int index = IndexOf(tabId);
        if (index < 0)
        {
            return _tabs.Count > 0;
        }

        bool wasActive = tabId == _appState.ActiveTabIdValue;

        // Pick the survivor before mutating, so bindings move off the closing
        // scope before it is disposed.
        Guid nextActive = _appState.ActiveTabIdValue;
        if (wasActive)
        {
            if (_tabs.Count <= 1)
            {
                nextActive = Guid.Empty;
            }
            else
            {
                int neighbour = index > 0 ? index - 1 : 1;
                nextActive = _tabs[neighbour].Id;
            }
        }

        TabEntry entry = _tabs[index];
        _tabs.RemoveAt(index);
        _byId.Remove(tabId);

        if (wasActive)
        {
            _appState.SetActiveTab(nextActive);
        }
        entry.Scope.Dispose();
        _tabsChanged.OnNext(Unit.Default);

        return _tabs.Count > 0;
    }

    /// <summary>Activates the tab at <paramref name="index"/>; no-op if out of range.</summary>
    public void ActivateByIndex(int index)
    {
        if (index >= 0 && index < _tabs.Count)
        {
            _appState.SetActiveTab(_tabs[index].Id);
        }
    }

    /// <summary>
    /// Activates the tab <paramref name="delta"/> positions from the active one.
    /// Wraps around the ends when <paramref name="wrap"/> is true.
    /// </summary>
    public void ActivateRelative(int delta, bool wrap = true)
    {
        if (_tabs.Count == 0)
        {
            return;
        }
        int current = IndexOf(_appState.ActiveTabIdValue);
        if (current < 0)
        {
            current = 0;
        }
        int next = current + delta;
        next = wrap
            ? ((next % _tabs.Count) + _tabs.Count) % _tabs.Count
            : Math.Clamp(next, 0, _tabs.Count - 1);
        _appState.SetActiveTab(_tabs[next].Id);
    }

    /// <summary>Moves the active tab <paramref name="delta"/> positions (clamped, no wrap).</summary>
    public void MoveActiveTab(int delta)
    {
        int from = IndexOf(_appState.ActiveTabIdValue);
        if (from >= 0)
        {
            MoveTab(_tabs[from].Id, from + delta);
        }
    }

    /// <summary>Moves <paramref name="tabId"/> to <paramref name="toIndex"/> (clamped). For drag &amp; drop.</summary>
    public void MoveTab(Guid tabId, int toIndex)
    {
        int from = IndexOf(tabId);
        if (from < 0)
        {
            return;
        }
        int to = Math.Clamp(toIndex, 0, _tabs.Count - 1);
        if (to == from)
        {
            return;
        }
        TabEntry entry = _tabs[from];
        _tabs.RemoveAt(from);
        _tabs.Insert(to, entry);
        _tabsChanged.OnNext(Unit.Default);
    }

    private int IndexOf(Guid tabId)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Id == tabId)
            {
                return i;
            }
        }
        return -1;
    }

    public void Dispose()
    {
        foreach (TabEntry entry in _tabs)
        {
            entry.Scope.Dispose();
        }
        _tabs.Clear();
        _byId.Clear();
        _tabsChanged.Dispose();
    }
}
