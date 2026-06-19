using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using R3;
using VExplorer.App.Features.Shell;
using VExplorer.Core.Completion;
using VExplorer.Core.FileSystem;
using VExplorer.Core.Modes;
using VExplorer.Core.State;

namespace VExplorer.App.Features.FileList;

public sealed partial class FileListViewModel
    : ObservableObject,
        IDisposable,
        VExplorer.App.Actions.ICursorTarget
{
    private readonly TabState _tabState;
    private readonly IDirectoryLister _lister;
    private readonly IShellInfoProvider _shellInfo;
    private readonly ILocationService _locationService;
    private readonly Core.State.Settings _settings;
    private readonly AppState _appState;
    private readonly RowEnricher _enricher;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private readonly IDisposable _subscription;
    private readonly IDisposable _selectionSubscription;
    private readonly IDisposable _modeSubscription;

    // Drives the debounced, FILTER-triggered re-listing used when the folder was too
    // large to list in full (see SetFilter). Carries the query (null = clear).
    private readonly Subject<string?> _filterRelistSubject = new();

    // Fires the unbounded ":loadall" re-listing that completes a truncated folder.
    private readonly Subject<Unit> _loadAllSubject = new();

    // SEARCH/FILTER state. ActiveFilter narrows the list; SearchMatches are display
    // indices that n/N cycle. SearchOrigin restores the cursor on Esc.
    private string? _activeFilter;
    private List<int> _searchMatches = [];
    private int _searchPos = -1;
    private int _searchOrigin;
    private Mode _prevMode = new Mode.Normal();

    /// <summary>True when the last listing was cut short by the time budget.</summary>
    private bool _isTruncated;

    /// <summary>True when the current raw items came from a filesystem-prefiltered listing.</summary>
    private bool _fsFilterActive;

    /// <summary>Cancels the background icon/type enrichment of a stale listing.</summary>
    private CancellationTokenSource? _enrichCts;

    /// <summary>Supersedes an in-flight (worker-thread) sort when a newer one starts.</summary>
    private int _sortVersion;

    /// <summary>
    /// When non-null, the next listing positions the cursor on the item whose name
    /// matches this (used to restore focus to the folder we came up from).
    /// </summary>
    private string? _returnToName;

    /// <summary>Raw (unsorted, lightweight) items rebuilt on every listing; excludes "..".</summary>
    private List<FileItem> _rawItems = [];

    /// <summary>Sorted + filtered lightweight items (excludes ".."); the scan layer.</summary>
    private IReadOnlyList<FileItem> _sortedItems = [];

    /// <summary>The parent location, when a ".." row should head the list; else null.</summary>
    private Location? _parent;

    /// <summary>The currently-bound virtualized row list (for selection push-down).</summary>
    private VirtualizedRowList? _rowList;

    [ObservableProperty]
    private IReadOnlyList<FileItemRow> _displayItems = Array.Empty<FileItemRow>();

    [ObservableProperty]
    private int _cursorIndex;

    /// <summary>
    /// The live SEARCH/FILTER query whose occurrences are highlighted in the Name
    /// column. Empty when neither is active (no highlighting).
    /// </summary>
    [ObservableProperty]
    private string _highlightQuery = "";

    /// <summary>Total non-parent rows before any FILTER narrowing (the "of N" count).</summary>
    [ObservableProperty]
    private int _totalItemCount;

    /// <summary>True while a FILTER is narrowing the list.</summary>
    [ObservableProperty]
    private bool _isFiltered;

    /// <summary>Number of SEARCH matches in the current list (0 when none / inactive).</summary>
    [ObservableProperty]
    private int _searchMatchCount;

    /// <summary>1-based position of the cursor among SEARCH matches (0 when not on one).</summary>
    [ObservableProperty]
    private int _searchMatchOrdinal;

    public string SortColumn { get; private set; } = "Name";
    public bool SortDescending { get; private set; } = false;

    /// <summary>Number of display rows including a leading ".." when present.</summary>
    private int DisplayCount => _sortedItems.Count + ParentOffset;

    private int ParentOffset => _parent != null ? 1 : 0;

    private bool IsParentIndex(int displayIndex)
    {
        return _parent != null && displayIndex == 0;
    }

    public FileListViewModel(
        TabState tabState,
        IDirectoryLister lister,
        IShellInfoProvider shellInfo,
        IIconImageCache iconCache,
        ILocationService locationService,
        Core.State.Settings settings,
        AppState appState
    )
    {
        _tabState = tabState;
        _lister = lister;
        _shellInfo = shellInfo;
        _locationService = locationService;
        _settings = settings;
        _appState = appState;
        _enricher = new RowEnricher(shellInfo, iconCache);

        // A single request stream feeds the list: folder changes, in-place refreshes
        // (after a file op), debounced FILTER re-listings (when the folder is too big
        // to fully list in memory), and the explicit ":loadall". AwaitOperation.Switch
        // drops a superseded listing's work so fast navigation never stalls.
        Observable<ListRequest> navigation = tabState.CurrentLocation.Select(l => new ListRequest(
            l,
            ListKind.Navigate,
            null,
            settings.ListTimeoutMs
        ));
        Observable<ListRequest> refresh = tabState.RefreshRequested.Select(_ => new ListRequest(
            tabState.CurrentLocationValue,
            ListKind.Refresh,
            _fsFilterActive ? _activeFilter : null,
            settings.ListTimeoutMs
        ));
        Observable<ListRequest> filterRelist = _filterRelistSubject
            .Debounce(TimeSpan.FromMilliseconds(settings.FilterDelayMs))
            .Select(q => new ListRequest(
                tabState.CurrentLocationValue,
                ListKind.FilterRelist,
                q,
                settings.ListTimeoutMs
            ));
        Observable<ListRequest> loadAll = _loadAllSubject.Select(_ => new ListRequest(
            tabState.CurrentLocationValue,
            ListKind.LoadAll,
            null,
            TimeoutMs: 0
        ));

        _subscription = navigation
            .Merge(refresh)
            .Merge(filterRelist)
            .Merge(loadAll)
            .SelectAwait(
                async (ListRequest req, CancellationToken cancel) =>
                {
                    tabState.SetLoading(true);
                    try
                    {
                        ListOptions opts = new()
                        {
                            NameFilter = req.FsFilter,
                            TimeoutMs = req.TimeoutMs,
                        };
                        IFileItemSource source = await lister.ListAsync(req.Loc, opts, cancel);
                        return (req, source, (string?)null);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return (
                            req,
                            (IFileItemSource)InMemoryFileItemSource.Empty,
                            $"Access denied: {req.Loc.DisplayName}"
                        );
                    }
                    catch (DirectoryNotFoundException)
                    {
                        return (
                            req,
                            (IFileItemSource)InMemoryFileItemSource.Empty,
                            $"Directory not found: {req.Loc.DisplayName}"
                        );
                    }
                    catch (Exception ex)
                    {
                        return (req, (IFileItemSource)InMemoryFileItemSource.Empty, ex.Message);
                    }
                },
                AwaitOperation.Switch
            )
            .ObserveOnCurrentDispatcher()
            .Subscribe(tuple =>
            {
                (ListRequest req, IFileItemSource source, string? error) = tuple;
                tabState.SetLoading(false);

                if (error != null)
                {
                    tabState.SetStatusMessage(error);
                }

                _isTruncated = source.IsTruncated;
                _fsFilterActive = req.FsFilter != null;

                // A fresh navigation or an explicit ":loadall" drops any search/filter;
                // a refresh or a FILTER re-listing keeps the active narrowing.
                if (req.Kind is ListKind.Navigate or ListKind.LoadAll)
                {
                    ClearSearchFilter();
                }

                // A just-operated item (rename/mkdir/paste/…) takes top priority for
                // the cursor; reuse the by-name restore via _returnToName.
                if (tabState.ConsumePendingFocusName() is string focusName)
                {
                    _returnToName = focusName;
                }

                // On a refresh, keep the cursor on the same item by name.
                string? preserveName =
                    req.Kind == ListKind.Refresh
                    && CursorIndex >= 0
                    && CursorIndex < DisplayItems.Count
                        ? DisplayItems[CursorIndex].Name
                        : null;

                List<FileItem> raw = new(source.Count);
                for (int i = 0; i < source.Count; i++)
                {
                    raw.Add(source[i]);
                }
                _rawItems = raw;
                _parent = _locationService.GetParent(req.Loc);

                // Indices change on every reload; drop the (now stale) selection.
                tabState.SetSelection(ImmutableHashSet<int>.Empty);

                if (_isTruncated)
                {
                    tabState.SetStatusMessage(
                        $"Listing stopped at {source.Count} items (timed out). "
                            + "Run :loadall to list everything.",
                        isError: false
                    );
                }

                ApplySort(preserveName);
            });

        // Project the central selection onto the realized row highlight flags.
        _selectionSubscription = tabState
            .Selection.ObserveOnCurrentDispatcher()
            .Subscribe(ApplySelectionToRows);

        // Drive incremental SEARCH / live FILTER off the central mode.
        _modeSubscription = tabState.Mode.ObserveOnCurrentDispatcher().Subscribe(ApplyMode);
    }

    /// <summary>Re-lists the current folder with no time budget (the ":loadall" command).</summary>
    public void LoadAll()
    {
        _loadAllSubject.OnNext(Unit.Default);
    }

    // Cursor movement

    public void MoveCursorDown()
    {
        if (DisplayItems.Count == 0)
        {
            return;
        }
        CursorIndex = Math.Min(CursorIndex + 1, DisplayItems.Count - 1);
    }

    public void MoveCursorUp()
    {
        CursorIndex = Math.Max(CursorIndex - 1, 0);
    }

    public void MoveCursorToTop()
    {
        CursorIndex = 0;
    }

    public void MoveCursorToBottom()
    {
        CursorIndex = Math.Max(0, DisplayItems.Count - 1);
    }

    public void MoveCursorPageUp(int step)
    {
        CursorIndex = Math.Max(0, CursorIndex - step);
    }

    public void MoveCursorPageDown(int step)
    {
        if (DisplayItems.Count == 0)
        {
            return;
        }
        CursorIndex = Math.Min(DisplayItems.Count - 1, CursorIndex + step);
    }

    // SEARCH / FILTER

    /// <summary>Reacts to SEARCH/FILTER mode changes (incremental jump / live narrow / cancel).</summary>
    private void ApplyMode(Mode mode)
    {
        // Leaving an unconfirmed SEARCH/FILTER undoes its preview, whether the
        // next mode is NORMAL or another submode (e.g. FILTER → SEARCH).
        if (_prevMode is Mode.Search { IsConfirmed: false } && mode is not Mode.Search)
        {
            // Search cancelled: drop matches and restore the cursor.
            _searchMatches = [];
            _searchPos = -1;
            HighlightQuery = "";
            SearchMatchCount = 0;
            SearchMatchOrdinal = 0;
            CursorIndex = ClampCursor(_searchOrigin, DisplayItems.Count);
        }
        else if (_prevMode is Mode.Filter { IsConfirmed: false } && mode is not Mode.Filter)
        {
            SetFilter(null); // Filter cancelled: drop the narrowing.
        }

        switch (mode)
        {
            case Mode.Search search:
                if (_prevMode is not Mode.Search)
                {
                    _searchOrigin = CursorIndex; // remember where to return on cancel
                }
                RecomputeSearch(search.Query);
                break;

            case Mode.Filter filter:
                SetFilter(filter.Query);
                break;
        }
        _prevMode = mode;
    }

    private void RecomputeSearch(string query)
    {
        HighlightQuery = query;
        _searchMatches = [];
        if (query.Length == 0)
        {
            _searchPos = -1;
            SearchMatchCount = 0;
            SearchMatchOrdinal = 0;
            return;
        }
        // Scan the lightweight layer so a search never realizes every display row.
        int offset = ParentOffset;
        for (int i = 0; i < _sortedItems.Count; i++)
        {
            FileItem item = _sortedItems[i];
            if (
                CompletionMatcher.IsMatch(
                    item.DisplayName ?? item.Name,
                    query,
                    _appState.Settings.Fuzzy
                )
            )
            {
                _searchMatches.Add(i + offset);
            }
        }
        SearchMatchCount = _searchMatches.Count;
        if (_searchMatches.Count == 0)
        {
            _searchPos = -1;
            SearchMatchOrdinal = 0;
            return;
        }
        // Jump to the first match at or after where search began, else wrap.
        int pos = _searchMatches.FindIndex(idx => idx >= _searchOrigin);
        _searchPos = pos >= 0 ? pos : 0;
        SearchMatchOrdinal = _searchPos + 1;
        CursorIndex = _searchMatches[_searchPos];
    }

    private void SetFilter(string? query)
    {
        string? next = string.IsNullOrEmpty(query) ? null : query;
        if (string.Equals(_activeFilter, next, StringComparison.Ordinal))
        {
            return;
        }
        _activeFilter = next;
        IsFiltered = next != null;
        HighlightQuery = next ?? "";

        // Adaptive FILTER: when the folder listed in full, narrow in memory (instant,
        // no re-scan). When it was truncated — or we are already showing a filesystem-
        // prefiltered listing — also kick off a debounced re-listing that filters at
        // the filesystem so matches beyond the truncation point are found too.
        ApplySort(CursorRow?.Name);
        if (_isTruncated || _fsFilterActive)
        {
            _filterRelistSubject.OnNext(next);
        }
    }

    /// <summary>n — move the cursor to the next SEARCH match (wraps).</summary>
    public void NextMatch()
    {
        MoveMatch(+1);
    }

    /// <summary>N — move the cursor to the previous SEARCH match (wraps).</summary>
    public void PrevMatch()
    {
        MoveMatch(-1);
    }

    private void MoveMatch(int direction)
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }
        int current = _searchMatches.IndexOf(CursorIndex);
        if (current >= 0)
        {
            _searchPos = current;
        }
        _searchPos = (_searchPos + direction + _searchMatches.Count) % _searchMatches.Count;
        SearchMatchOrdinal = _searchPos + 1;
        CursorIndex = _searchMatches[_searchPos];
    }

    /// <summary>Clears any active filter (NORMAL Esc). Returns true if one was cleared.</summary>
    public bool ClearFilter()
    {
        if (_activeFilter == null)
        {
            return false;
        }
        SetFilter(null);
        return true;
    }

    private void ClearSearchFilter()
    {
        _activeFilter = null;
        _searchMatches = [];
        _searchPos = -1;
        HighlightQuery = "";
        IsFiltered = false;
        SearchMatchCount = 0;
        SearchMatchOrdinal = 0;
    }

    // Navigation

    /// <summary>
    /// Navigate into the directory under the cursor (bound to <c>l</c> / Right).
    /// No-op for files.
    /// </summary>
    public void NavigateIntoCurrent(TabState tabState)
    {
        if (DisplayItems.Count == 0)
        {
            return;
        }
        FileItemRow row = DisplayItems[CursorIndex];
        if (row.IsParentEntry)
        {
            NavigateToParent(tabState);
        }
        else if (InRecycleBin(tabState))
        {
            // Deleted items are not navigable in place; restore them first.
        }
        else if (row.IsDirectory)
        {
            tabState.NavigateTo(row.Location);
        }
    }

    /// <summary>
    /// Activate the item under the cursor (bound to <c>Enter</c> and double-click).
    /// </summary>
    public void ActivateCurrentItem(TabState tabState)
    {
        if (DisplayItems.Count == 0)
        {
            return;
        }
        FileItemRow row = DisplayItems[CursorIndex];
        if (row.IsParentEntry)
        {
            NavigateToParent(tabState);
        }
        else if (InRecycleBin(tabState))
        {
            // Deleted items are not navigable / launchable; no-op (restore first).
        }
        else if (row.IsDirectory)
        {
            tabState.NavigateTo(row.Location);
        }
        else
        {
            Process.Start(new ProcessStartInfo(row.FullPath) { UseShellExecute = true });
        }
    }

    private static bool InRecycleBin(TabState tabState)
    {
        return KnownLocations.IsRecycleBin(tabState.CurrentLocationValue);
    }

    /// <summary>
    /// Navigate to the parent location (bound to <c>h</c> / Left / Backspace).
    /// Remembers the current folder name so the cursor lands on it after loading.
    /// </summary>
    public void NavigateToParent(TabState tabState)
    {
        Location current = tabState.CurrentLocationValue;
        _returnToName = current.TryGetFileSystemPath(out string path)
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))
            : current.DisplayName;
        if (_locationService.GetParent(current) is Location parent)
        {
            tabState.NavigateTo(parent);
        }
    }

    // Selection helpers

    /// <summary>The row under the cursor, or null when out of range.</summary>
    public FileItemRow? CursorRow =>
        CursorIndex >= 0 && CursorIndex < DisplayItems.Count ? DisplayItems[CursorIndex] : null;

    public FileItemRow? RowAt(int index)
    {
        return index >= 0 && index < DisplayItems.Count ? DisplayItems[index] : null;
    }

    /// <summary>
    /// The operation targets: the multi-selection if any (excluding ".."), else
    /// the cursor row (excluding "..").
    /// </summary>
    public IReadOnlyList<string> ResolveTargetPaths()
    {
        // Deleted items in the Recycle Bin are operated on via :restore / :delete,
        // not the generic filesystem operations (their paths are not live).
        if (KnownLocations.IsRecycleBin(_tabState.CurrentLocationValue))
        {
            return [];
        }

        ImmutableHashSet<int> sel = _tabState.SelectionValue;
        if (sel.Count > 0)
        {
            int offset = ParentOffset;
            List<string> result = [];
            for (int i = 0; i < _sortedItems.Count; i++)
            {
                if (
                    sel.Contains(i + offset)
                    && _sortedItems[i].ResolveLocation().TryGetFileSystemPath(out string p)
                )
                {
                    result.Add(p);
                }
            }
            return result;
        }
        FileItemRow? cursor = CursorRow;
        if (cursor is { IsParentEntry: false } && ToFileSystemPath(cursor) is string path)
        {
            return [path];
        }
        return [];
    }

    /// <summary>The row's filesystem path for file operations, or null for non-filesystem rows.</summary>
    private static string? ToFileSystemPath(FileItemRow row)
    {
        return row.Location.TryGetFileSystemPath(out string p) ? p : null;
    }

    /// <summary>
    /// Shell-source tokens for the operation targets (selection else cursor),
    /// used for Recycle Bin restore / permanent-delete. Excludes "..".
    /// </summary>
    public IReadOnlyList<int> ResolveTargetTokens()
    {
        ImmutableHashSet<int> sel = _tabState.SelectionValue;
        if (sel.Count > 0)
        {
            int offset = ParentOffset;
            List<int> result = [];
            for (int i = 0; i < _sortedItems.Count; i++)
            {
                if (sel.Contains(i + offset) && _sortedItems[i].ShellToken is int token)
                {
                    result.Add(token);
                }
            }
            return result;
        }
        FileItemRow? cursor = CursorRow;
        if (cursor is { IsParentEntry: false, ShellToken: int t })
        {
            return [t];
        }
        return [];
    }

    /// <summary>Returns the selection with <paramref name="index"/> toggled (".." ignored).</summary>
    public ImmutableHashSet<int> ToggleSelection(ImmutableHashSet<int> current, int index)
    {
        if (index < 0 || index >= DisplayCount || IsParentIndex(index))
        {
            return current;
        }
        return current.Contains(index) ? current.Remove(index) : current.Add(index);
    }

    /// <summary>Selection covering the contiguous range anchor..cursor (".." excluded).</summary>
    public ImmutableHashSet<int> RangeSelection(int anchor, int cursor)
    {
        int lo = Math.Min(anchor, cursor);
        int hi = Math.Max(anchor, cursor);
        ImmutableHashSet<int>.Builder builder = ImmutableHashSet.CreateBuilder<int>();
        for (int i = lo; i <= hi; i++)
        {
            if (i >= 0 && i < DisplayCount && !IsParentIndex(i))
            {
                builder.Add(i);
            }
        }
        return builder.ToImmutable();
    }

    // Sorting

    public void SortBy(string column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = false;
        }

        ApplySort(CursorRow?.Name);
    }

    // Helpers

    /// <summary>
    /// Filters and sorts the lightweight items on a worker thread (so a huge folder
    /// never blocks the UI), then rebinds a freshly virtualized row list and restores
    /// the cursor on the dispatcher. A version guard drops a superseded pass.
    /// </summary>
    private void ApplySort(string? preserveName = null)
    {
        List<FileItem> raw = _rawItems;
        string? filter = _activeFilter;
        string column = SortColumn;
        bool descending = SortDescending;
        Location? parent = _parent;
        string? returnTo = _returnToName;
        _returnToName = null;
        int version = ++_sortVersion;
        bool fuzzy = _appState.Settings.Fuzzy;
        bool foldersFirst = _appState.Settings.FoldersFirst;

        TotalItemCount = raw.Count;

        // The Type column sorts on the shell type name; resolve it on the worker
        // thread (cached by extension, so this is cheap after warm-up).
        Func<FileItem, string>? typeKey = column == "Type" ? item => ResolveTypeName(item) : null;

        Task.Run(() =>
        {
            List<FileItem> sorted = FileListProjection.SortAndFilter(
                raw,
                filter,
                column,
                descending,
                typeKey,
                fuzzy,
                foldersFirst
            );
            _dispatcher.BeginInvoke(() =>
            {
                if (version != _sortVersion)
                {
                    return; // a newer sort superseded this one
                }
                ApplySorted(sorted, parent, preserveName, returnTo);
            });
        });
    }

    private void ApplySorted(
        List<FileItem> sorted,
        Location? parent,
        string? preserveName,
        string? returnTo
    )
    {
        _sortedItems = sorted;
        _parent = parent;
        int offset = parent != null ? 1 : 0;

        // Reset enrichment so the new row set resolves fresh and any stale pass stops.
        _enrichCts?.Cancel();
        _enrichCts?.Dispose();
        _enrichCts = new CancellationTokenSource();
        CancellationToken enrichToken = _enrichCts.Token;

        int count = sorted.Count + offset;
        VirtualizedRowList list = new(
            count,
            i => CreateRow(i, sorted, parent, offset, enrichToken)
        );
        _rowList = list;
        DisplayItems = list;

        // Cursor restoration priority: up-navigation target → preserve-by-name → top.
        string? target = returnTo ?? preserveName;
        if (target != null)
        {
            int idx = -1;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (string.Equals(sorted[i].Name, target, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i + offset;
                    break;
                }
            }
            CursorIndex = idx >= 0 ? idx : ClampCursor(CursorIndex, count);
        }
        else
        {
            CursorIndex = 0;
        }
    }

    private FileItemRow CreateRow(
        int displayIndex,
        IReadOnlyList<FileItem> sorted,
        Location? parent,
        int offset,
        CancellationToken enrichToken
    )
    {
        FileItemRow row =
            parent is Location p && displayIndex == 0
                ? FileItemRow.CreateParentEntry(p)
                : FileItemRow.FromFileItem(sorted[displayIndex - offset]);
        row.Index = displayIndex;
        row.IsSelected = !row.IsParentEntry && _tabState.SelectionValue.Contains(displayIndex);
        _enricher.Enrich(row, enrichToken);
        return row;
    }

    private string ResolveTypeName(FileItem item)
    {
        ShellItemInfo info = _shellInfo.Resolve(
            item.ResolveLocation(),
            item.Extension,
            item.IsDirectory
        );
        return string.IsNullOrEmpty(info.TypeName)
            ? FileListProjection.FallbackTypeLabel(item)
            : info.TypeName;
    }

    /// <summary>Clamp an index into range, avoiding the ".." row unless it is the only one.</summary>
    private static int ClampCursor(int index, int count)
    {
        if (count == 0)
        {
            return 0;
        }
        int clamped = Math.Clamp(index, 0, count - 1);
        return clamped == 0 && count > 1 ? 1 : clamped;
    }

    private void ApplySelectionToRows(ImmutableHashSet<int> selection)
    {
        if (_rowList == null)
        {
            return;
        }
        // Only realized (visible) rows need updating; rows created later read the
        // selection at construction time.
        foreach (FileItemRow row in _rowList.RealizedRows)
        {
            row.IsSelected = !row.IsParentEntry && selection.Contains(row.Index);
        }
    }

    public void Dispose()
    {
        _enrichCts?.Cancel();
        _enrichCts?.Dispose();
        _subscription.Dispose();
        _selectionSubscription.Dispose();
        _modeSubscription.Dispose();
        _filterRelistSubject.Dispose();
        _loadAllSubject.Dispose();
    }
}

/// <summary>What triggered a listing — controls filter/selection handling on completion.</summary>
internal enum ListKind
{
    Navigate,
    Refresh,
    FilterRelist,
    LoadAll,
}

/// <summary>A single listing request fed into the file-list pipeline.</summary>
internal readonly record struct ListRequest(
    Location Loc,
    ListKind Kind,
    string? FsFilter,
    int TimeoutMs
);
