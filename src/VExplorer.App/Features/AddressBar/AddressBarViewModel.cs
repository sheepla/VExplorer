using R3;
using VExplorer.App.Features.Completion;
using VExplorer.Core.Completion;
using VExplorer.Core.FileSystem;
using VExplorer.Core.Modes;
using VExplorer.Core.State;
using CoreSettings = VExplorer.Core.State.Settings;

namespace VExplorer.App.Features.AddressBar;

/// <summary>
/// Drives the top address bar. Shows the current path as plain text when not
/// focused; in ADDRESS mode it becomes an editable buffer with path/special
/// folder completion. Editing/cycling behaviour is shared via
/// <see cref="CompletionEditorViewModel"/>.
/// </summary>
public sealed class AddressBarViewModel : CompletionEditorViewModel
{
    private readonly AddressContextResolver _resolver = new();
    private readonly IDisposable _dirSubscription;
    private readonly ILocationService _locationService;

    public AddressBarViewModel(
        TabState tabState,
        CompletionEngine engine,
        CoreSettings settings,
        ILocationService locationService
    )
        : base(tabState, engine, settings.AddressBarDelayMs)
    {
        _locationService = locationService;
        // When not editing, mirror the current location as plain text: the
        // filesystem path when it has one, else the shell display name.
        _dirSubscription = tabState
            .CurrentLocation.ObserveOnCurrentDispatcher()
            .Subscribe(loc =>
            {
                if (!IsActive)
                {
                    SetTextSilently(loc.TryGetFileSystemPath(out string p) ? p : loc.DisplayName);
                }
            });
    }

    protected override bool IsActiveMode(Mode mode)
    {
        return mode is Mode.Address;
    }

    protected override void EnterMode()
    {
        TabState.DispatchModeEvent(new ModeEvent.EnterAddress());
    }

    protected override CompletionContext ResolveContext(
        string buffer,
        int caret,
        string currentDirectory
    )
    {
        return _resolver.Resolve(buffer, caret, currentDirectory);
    }

    // Seed the editor with the current path so the user edits from it.
    protected override string SeedText()
    {
        Location loc = TabState.CurrentLocationValue;
        return loc.TryGetFileSystemPath(out string p) ? p : loc.DisplayName;
    }

    protected override void OnConfirm(string text)
    {
        string target = text.Trim();
        // Accepts filesystem paths, known location names, and shell parsing names.
        if (_locationService.TryResolve(target, out Location location))
        {
            ClearCandidates();
            TabState.NavigateTo(location);
            TabState.DispatchModeEvent(new ModeEvent.ConfirmMode());
        }
        else
        {
            // Do not move; leave the buffer intact so it can be edited.
            TabState.SetStatusMessage($"Path not found: {target}");
        }
    }

    protected override void OnDispose()
    {
        _dirSubscription.Dispose();
    }
}
