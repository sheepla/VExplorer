using System.Windows.Threading;
using VExplorer.Core.FileSystem;

namespace VExplorer.App.Features.Menu;

/// <summary>
/// Runs all shell context-menu COM work on a dedicated STA thread with a message pump, so a
/// slow <c>QueryContextMenu</c> (heavy third-party shell extensions) never blocks the UI. The
/// <c>IContextMenu</c> is apartment-affine, so its whole lifetime — extract, expand, invoke,
/// dispose — stays on this one worker thread; the UI talks to it only through the async wrappers.
/// </summary>
public sealed class ShellMenuHost(IShellContextMenu shell)
{
    private readonly IShellContextMenu _shell = shell;
    private readonly object _gate = new();
    private Dispatcher? _dispatcher;

    private Dispatcher Worker()
    {
        if (_dispatcher is { } ready)
        {
            return ready;
        }
        lock (_gate)
        {
            if (_dispatcher is { } existing)
            {
                return existing;
            }
            using ManualResetEventSlim started = new();
            Dispatcher? created = null;
            Thread thread = new(() =>
            {
                created = Dispatcher.CurrentDispatcher;
                started.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "ShellMenuWorker",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            started.Wait();
            _dispatcher = created!;
            return _dispatcher;
        }
    }

    public async Task<HostedMenuSession?> OpenForItemsAsync(IReadOnlyList<string> paths, nint hwnd)
    {
        Dispatcher worker = Worker();
        IShellMenuSession? session = await worker.InvokeAsync(() =>
            _shell.OpenForItems(paths, hwnd)
        );
        return session is null ? null : new HostedMenuSession(session, worker);
    }

    public async Task<HostedMenuSession?> OpenForFolderBackgroundAsync(string folder, nint hwnd)
    {
        Dispatcher worker = Worker();
        IShellMenuSession? session = await worker.InvokeAsync(() =>
            _shell.OpenForFolderBackground(folder, hwnd)
        );
        return session is null ? null : new HostedMenuSession(session, worker);
    }
}

/// <summary>
/// UI-facing handle to a shell menu session living on the worker thread. Every call marshals to
/// the worker's dispatcher. <see cref="TopLevelItems"/> is fixed at open time (already on the
/// worker), so reading it from the UI is safe.
/// </summary>
public sealed class HostedMenuSession(IShellMenuSession session, Dispatcher worker)
{
    private readonly IShellMenuSession _session = session;
    private readonly Dispatcher _worker = worker;
    private bool _disposed;

    public IReadOnlyList<ShellMenuItem> TopLevelItems => _session.TopLevelItems;

    public Task<IReadOnlyList<ShellMenuItem>> ExpandSubmenuAsync(int itemId)
    {
        return _worker.InvokeAsync(() => _session.ExpandSubmenu(itemId)).Task;
    }

    public Task InvokeAsync(int itemId)
    {
        return _worker.InvokeAsync(() => _session.Invoke(itemId)).Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        // Fire-and-forget on the worker; queued after any in-flight Invoke (FIFO), so a click
        // that invokes then closes still runs the command before the menu is torn down.
        _worker.InvokeAsync(_session.Dispose);
    }
}
