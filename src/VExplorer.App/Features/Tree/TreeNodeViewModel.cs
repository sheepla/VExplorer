using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VExplorer.App.Features.Shell;
using VExplorer.Core.FileSystem;

namespace VExplorer.App.Features.Tree;

public sealed partial class TreeNodeViewModel : ObservableObject
{
    private readonly IDirectoryLister? _lister;
    private readonly IShellInfoProvider? _shellInfo;
    private readonly IIconImageCache? _iconCache;
    private readonly Core.State.Settings? _settings;
    private readonly bool _isSentinel;
    private bool _isLoadStarted;
    private bool _isLoadComplete;
    private bool _capLifted;
    private TaskCompletionSource? _loadingTcs;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _displayName = "";

    /// <summary>The node's navigable location (shell identity for PC/special/virtual, else a path).</summary>
    public Location Location { get; }

    /// <summary>The node's filesystem path when it has one; empty for shell-only nodes.</summary>
    public string FullPath { get; } = "";

    /// <summary>Small shell folder icon, resolved off the UI thread.</summary>
    [ObservableProperty]
    private BitmapSource? _icon;

    /// <summary>True for the not-yet-loaded stand-in child (drives the expander arrow).</summary>
    public bool IsPlaceholder => _lister == null && !_isSentinel;

    /// <summary>
    /// True for the "… (N more)" row appended when a folder has more children than
    /// the display cap. Activating it loads the rest (see <see cref="OverflowParent"/>).
    /// </summary>
    public bool IsOverflowSentinel => _isSentinel;

    /// <summary>The folder whose remaining children this sentinel reveals; null otherwise.</summary>
    internal TreeNodeViewModel? OverflowParent { get; }

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public TreeNodeViewModel(
        IDirectoryLister lister,
        IShellInfoProvider shellInfo,
        IIconImageCache iconCache,
        Core.State.Settings settings,
        Location location,
        string displayName
    )
    {
        _lister = lister;
        _shellInfo = shellInfo;
        _iconCache = iconCache;
        _settings = settings;
        Location = location;
        location.TryGetFileSystemPath(out string path);
        FullPath = path;
        _displayName = displayName;
        Children.Add(new TreeNodeViewModel());
    }

    /// <summary>Loading placeholder constructor (no lister → <see cref="IsPlaceholder"/>).</summary>
    private TreeNodeViewModel() { }

    /// <summary>Overflow-sentinel constructor ("… (N more)"); reveals <paramref name="parent"/>'s rest.</summary>
    private TreeNodeViewModel(int remaining, TreeNodeViewModel parent)
    {
        _isSentinel = true;
        OverflowParent = parent;
        Location = parent.Location;
        _displayName = $"… ({remaining} more)";
    }

    /// <summary>
    /// Resolves this node's folder icon. Intended to be called on a worker thread;
    /// the returned bitmap is frozen and assigned to <see cref="Icon"/> on the UI
    /// thread by the caller.
    /// </summary>
    internal BitmapSource? ResolveIcon()
    {
        if (_shellInfo is null || _iconCache is null)
        {
            return null;
        }
        // Filesystem nodes resolve by path; shell-namespace nodes (PC, special
        // folders, Recycle Bin, Network) resolve via their PIDL for the real icon.
        ShellItemInfo info = _shellInfo.Resolve(Location, "", isDirectory: true);
        return _iconCache.GetIcon(
            info.SystemIconIndex,
            () => _shellInfo.CopyIcon(Location, "", isDirectory: true),
            isDirectory: true
        );
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (_isSentinel || IsPlaceholder)
        {
            return;
        }
        if (value)
        {
            if (!_isLoadStarted)
            {
                _isLoadStarted = true;
                _ = LoadChildrenAsync();
            }
        }
        else
        {
            // Free the (potentially huge) subtree on collapse; it reloads lazily on
            // the next expand. This keeps memory bounded when browsing big trees.
            ForgetChildren();
        }
    }

    public Task WaitForChildrenLoadedAsync()
    {
        if (_isLoadComplete)
        {
            return Task.CompletedTask;
        }
        _loadingTcs ??= new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        return _loadingTcs.Task;
    }

    /// <summary>Reloads this folder's children with the display cap removed (sentinel activation).</summary>
    internal Task RevealAllChildrenAsync()
    {
        if (_capLifted)
        {
            return Task.CompletedTask;
        }
        _capLifted = true;
        return LoadChildrenAsync();
    }

    private void ForgetChildren()
    {
        if (!_isLoadStarted)
        {
            return;
        }
        _isLoadStarted = false;
        _isLoadComplete = false;
        _capLifted = false;
        _loadingTcs = null;
        Children.Clear();
        Children.Add(new TreeNodeViewModel());
    }

    private async Task LoadChildrenAsync()
    {
        // 0 / negative cap means "unlimited"; a lifted cap shows everything.
        int cap = _capLifted ? int.MaxValue : _settings?.TreeChildrenCap ?? 0;
        if (cap <= 0)
        {
            cap = int.MaxValue;
        }

        List<TreeNodeViewModel> newChildren = [];
        int omitted = 0;
        try
        {
            // Bound the tree enumeration with the same time budget as the list, so a
            // huge folder cannot stall the tree either.
            ListOptions options = new() { TimeoutMs = _settings?.ListTimeoutMs ?? 0 };
            IFileItemSource source = await _lister!.ListAsync(
                Location,
                options,
                CancellationToken.None
            );
            for (int i = 0; i < source.Count; i++)
            {
                FileItem item = source[i];
                if (!item.IsDirectory)
                {
                    continue;
                }
                if (newChildren.Count < cap)
                {
                    newChildren.Add(
                        new TreeNodeViewModel(
                            _lister,
                            _shellInfo!,
                            _iconCache!,
                            _settings!,
                            item.ResolveLocation(),
                            item.DisplayName ?? item.Name
                        )
                    );
                }
                else
                {
                    omitted++;
                }
            }
        }
        catch (Exception) { }

        // Resolve icons while still on the worker thread (folders almost always
        // hit the shared cache, so this is near-free).
        BitmapSource?[] icons = new BitmapSource?[newChildren.Count];
        for (int i = 0; i < newChildren.Count; i++)
        {
            icons[i] = newChildren[i].ResolveIcon();
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            Children.Clear();
            for (int i = 0; i < newChildren.Count; i++)
            {
                newChildren[i].Icon = icons[i];
                Children.Add(newChildren[i]);
            }
            if (omitted > 0)
            {
                Children.Add(new TreeNodeViewModel(omitted, this));
            }
            _isLoadComplete = true;
            _loadingTcs?.TrySetResult();
        });
    }
}
