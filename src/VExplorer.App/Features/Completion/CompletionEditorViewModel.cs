using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using R3;
using VExplorer.Core.Completion;
using VExplorer.Core.Modes;
using VExplorer.Core.State;

namespace VExplorer.App.Features.Completion;

/// <summary>
/// Shared behaviour for a single-line editor with zsh-style Tab completion
/// (the address bar and the command bar). Owns the debounced, switch-latest
/// completion pipeline and the immutable <see cref="CompletionSession"/> used
/// for cycling. Subclasses provide the mode they bind to, how the buffer maps
/// to a completion context, the seed text and the confirm behaviour.
/// </summary>
public abstract partial class CompletionEditorViewModel : ObservableObject, IDisposable
{
    protected TabState TabState { get; }

    private readonly CompletionEngine _engine;
    private readonly Subject<string> _querySubject = new();
    private readonly IDisposable _modeSubscription;
    private readonly IDisposable _completionSubscription;

    /// <summary>Candidates from the last completed query (basis for Tab cycling).</summary>
    private IReadOnlyList<CompletionCandidate> _latestCandidates = [];

    /// <summary>The active Tab cycle, or <c>null</c> before the first Tab.</summary>
    private CompletionSession? _session;

    /// <summary>
    /// True while we mutate <see cref="Text"/> ourselves (seed, cycle, reset),
    /// suppressing the completion re-trigger so cycling does not rebuild the
    /// candidate list and reset the selection.
    /// </summary>
    private bool _suppressCompletion;

    /// <summary>
    /// Tracks the last observed active state so the mode subscription only
    /// re-seeds <see cref="Text"/> on an enter/leave transition. Buffer-only
    /// updates (each keystroke re-emits the mode) must not reset the text.
    /// </summary>
    private bool _wasActive;

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPopupOpen;

    [ObservableProperty]
    private int _selectedCandidateIndex = -1;

    public ObservableCollection<CompletionItemViewModel> Candidates { get; } = [];

    protected CompletionEditorViewModel(TabState tabState, CompletionEngine engine, int debounceMs)
    {
        TabState = tabState;
        _engine = engine;

        _modeSubscription = tabState.Mode.ObserveOnCurrentDispatcher().Subscribe(OnModeChanged);

        _completionSubscription = _querySubject
            .Debounce(TimeSpan.FromMilliseconds(debounceMs))
            .SelectAwait(
                async (string query, CancellationToken cancel) =>
                {
                    CompletionContext ctx = ResolveContext(
                        query,
                        query.Length,
                        TabState.CurrentDirectoryPath ?? ""
                    );
                    List<CompletionCandidate> list = [];
                    await foreach (
                        CompletionCandidate candidate in _engine.GetCandidatesAsync(ctx, cancel)
                    )
                    {
                        list.Add(candidate);
                    }
                    return (query, list);
                },
                AwaitOperation.Switch
            )
            .ObserveOnCurrentDispatcher()
            .Subscribe(result => ShowCandidates(result.query, result.list));
    }

    // Subclass contract

    /// <summary>Whether <paramref name="mode"/> is this editor's active mode.</summary>
    protected abstract bool IsActiveMode(Mode mode);

    /// <summary>Enters this editor's mode (dispatched on click / shortcut).</summary>
    protected abstract void EnterMode();

    /// <summary>Maps the current buffer + caret to a completion context.</summary>
    protected abstract CompletionContext ResolveContext(
        string buffer,
        int caret,
        string currentDirectory
    );

    /// <summary>Text to show when entering/leaving the mode (empty by default).</summary>
    protected virtual string SeedText()
    {
        return "";
    }

    /// <summary>Handles Enter; the subclass decides whether to act and/or exit.</summary>
    protected abstract void OnConfirm(string text);

    /// <summary>Hook for an active-state transition (e.g. extra subscriptions).</summary>
    protected virtual void OnActiveChanged(bool active) { }

    /// <summary>Hook for subclasses to dispose extra resources.</summary>
    protected virtual void OnDispose() { }

    // Text input

    partial void OnTextChanged(string value)
    {
        if (_suppressCompletion || !IsActive)
        {
            return;
        }
        // User-driven edit: keep the mode buffer in sync and recompute candidates.
        TabState.DispatchModeEvent(new ModeEvent.UpdateBuffer(value));
        _session = null;
        _querySubject.OnNext(value);
    }

    // Tab cycling / candidate navigation

    public void CycleNext()
    {
        Cycle(forward: true);
    }

    public void CyclePrev()
    {
        Cycle(forward: false);
    }

    private void Cycle(bool forward)
    {
        if (_latestCandidates.Count == 0)
        {
            return;
        }

        // First Tab seeds the selection (first or last); later Tabs advance it.
        _session = _session is null
            ? new CompletionSession(
                _latestCandidates,
                SelectedIndex: forward ? 0 : _latestCandidates.Count - 1,
                TokenStart: ResolveContext(
                    Text,
                    Text.Length,
                    TabState.CurrentDirectoryPath ?? ""
                ).TokenStart,
                OriginalBuffer: Text
            )
            : (forward ? _session.MoveNext() : _session.MovePrev());

        SetTextSilently(_session.RenderBuffer());
        SelectedCandidateIndex = _session.SelectedIndex;
        TabState.DispatchModeEvent(new ModeEvent.UpdateBuffer(Text));
    }

    /// <summary>Accept the candidate at <paramref name="index"/> (mouse click).</summary>
    public void SelectCandidate(int index)
    {
        if (index < 0 || index >= _latestCandidates.Count)
        {
            return;
        }
        int tokenStart = ResolveContext(
            Text,
            Text.Length,
            TabState.CurrentDirectoryPath ?? ""
        ).TokenStart;
        _session = new CompletionSession(_latestCandidates, index, tokenStart, Text);
        SetTextSilently(_session.RenderBuffer());
        SelectedCandidateIndex = index;
        TabState.DispatchModeEvent(new ModeEvent.UpdateBuffer(Text));
    }

    /// <summary>
    /// Ctrl+Backspace: delete one trailing path segment (e.g. <c>C:\Users\Pub</c>
    /// → <c>C:\Users\</c>). Re-runs completion for the shortened buffer.
    /// </summary>
    public void DeleteSegment()
    {
        string t = Text;
        if (t.Length == 0)
        {
            return;
        }
        int end = t.Length;
        if (t[end - 1] is '\\' or '/')
        {
            end--;
        }
        int sep = t.LastIndexOfAny(['\\', '/'], Math.Max(0, end - 1));
        Text = sep >= 0 ? t[..(sep + 1)] : "";
    }

    // Confirm / cancel / enter

    public void Confirm()
    {
        OnConfirm(Text);
    }

    public void Cancel()
    {
        TabState.DispatchModeEvent(new ModeEvent.ExitToNormal());
    }

    /// <summary>Enter this editor's mode (e.g. clicking it). No-op if already active.</summary>
    public void BeginEdit()
    {
        if (!IsActive)
        {
            EnterMode();
        }
    }

    // State plumbing

    private void OnModeChanged(Mode mode)
    {
        bool active = IsActiveMode(mode);
        if (active == _wasActive)
        {
            // Buffer-only change within the same mode; leave text/candidates.
            return;
        }

        _wasActive = active;
        IsActive = active;
        ClearCandidates();
        SetTextSilently(SeedText());
        OnActiveChanged(active);
    }

    private void ShowCandidates(string query, IReadOnlyList<CompletionCandidate> candidates)
    {
        // Ignore stale results that no longer match the current buffer.
        if (!IsActive || query != Text)
        {
            return;
        }

        _latestCandidates = candidates;
        _session = null;
        SelectedCandidateIndex = -1;

        Candidates.Clear();
        foreach (CompletionCandidate candidate in candidates)
        {
            Candidates.Add(new CompletionItemViewModel(candidate));
        }
        IsPopupOpen = candidates.Count > 0;
    }

    protected void ClearCandidates()
    {
        _latestCandidates = [];
        _session = null;
        Candidates.Clear();
        SelectedCandidateIndex = -1;
        IsPopupOpen = false;
    }

    protected void SetTextSilently(string value)
    {
        _suppressCompletion = true;
        Text = value;
        _suppressCompletion = false;
    }

    public void Dispose()
    {
        _modeSubscription.Dispose();
        _completionSubscription.Dispose();
        _querySubject.Dispose();
        OnDispose();
    }
}
