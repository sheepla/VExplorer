using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using R3;
using VExplorer.App.Features.Shell;
using VExplorer.Core.FileSystem;
using VExplorer.Core.State;

namespace VExplorer.App.Features.Tree;

public sealed partial class TreeViewModel
    : ObservableObject,
        IDisposable,
        VExplorer.App.Actions.ICursorTarget
{
    public ObservableCollection<TreeNodeViewModel> Roots { get; } = [];

    [ObservableProperty]
    private TreeNodeViewModel? _selectedNode;

    private readonly IDisposable _pathSubscription;

    public TreeViewModel(
        IDirectoryLister lister,
        IShellInfoProvider shellInfo,
        IIconImageCache iconCache,
        Core.State.Settings settings,
        TabState tabState
    )
    {
        // A single synthetic "PC" root holds the physical drives and special
        // folders as its lazily-loaded children (listed via the virtual lister).
        TreeNodeViewModel pcRoot = new(
            lister,
            shellInfo,
            iconCache,
            settings,
            KnownLocations.Pc,
            KnownLocations.Pc.DisplayName
        );
        Roots.Add(pcRoot);

        // Expand the PC root immediately so the drives are visible at startup.
        pcRoot.IsExpanded = true;

        _pathSubscription = tabState
            .CurrentLocation.ObserveOnCurrentDispatcher()
            .Subscribe(location => _ = NavigateToLocationAsync(location));
    }

    /// <summary>The visible (non-placeholder, depth-first) nodes — used for scroll margin.</summary>
    public IReadOnlyList<TreeNodeViewModel> VisibleNodes => GetVisibleNodes();

    // Cursor movement

    public void MoveCursorToTop()
    {
        List<TreeNodeViewModel> visible = GetVisibleNodes();
        if (visible.Count > 0)
        {
            SelectNode(visible[0]);
        }
    }

    public void MoveCursorToBottom()
    {
        List<TreeNodeViewModel> visible = GetVisibleNodes();
        if (visible.Count > 0)
        {
            SelectNode(visible[^1]);
        }
    }

    public void MoveCursorPageUp(int step)
    {
        List<TreeNodeViewModel> visible = GetVisibleNodes();
        if (visible.Count == 0)
        {
            return;
        }
        int idx = SelectedNode != null ? visible.IndexOf(SelectedNode) : 0;
        SelectNode(visible[Math.Max(0, idx - step)]);
    }

    public void MoveCursorPageDown(int step)
    {
        List<TreeNodeViewModel> visible = GetVisibleNodes();
        if (visible.Count == 0)
        {
            return;
        }
        int idx = SelectedNode != null ? visible.IndexOf(SelectedNode) : 0;
        SelectNode(visible[Math.Min(visible.Count - 1, idx + step)]);
    }

    public void MoveCursorDown()
    {
        List<TreeNodeViewModel> visible = GetVisibleNodes();
        if (visible.Count == 0)
        {
            return;
        }
        int idx = SelectedNode != null ? visible.IndexOf(SelectedNode) : -1;
        SelectNode(visible[Math.Min(idx + 1, visible.Count - 1)]);
    }

    public void MoveCursorUp()
    {
        List<TreeNodeViewModel> visible = GetVisibleNodes();
        if (visible.Count == 0)
        {
            return;
        }
        int idx = SelectedNode != null ? visible.IndexOf(SelectedNode) : 0;
        SelectNode(visible[Math.Max(idx - 1, 0)]);
    }

    // Expand / collapse (bound to l / h while Tree has focus)

    public void ExpandSelected()
    {
        if (SelectedNode == null)
        {
            return;
        }
        // The "… (N more)" row loads the rest of its folder instead of expanding.
        if (SelectedNode.IsOverflowSentinel)
        {
            _ = SelectedNode.OverflowParent?.RevealAllChildrenAsync();
            return;
        }
        if (!SelectedNode.IsExpanded)
        {
            SelectedNode.IsExpanded = true;
        }
        else
        {
            // Already expanded: move cursor to first child (ranger-style 'l')
            List<TreeNodeViewModel> visible = GetVisibleNodes();
            int idx = visible.IndexOf(SelectedNode);
            if (idx >= 0 && idx + 1 < visible.Count)
            {
                SelectNode(visible[idx + 1]);
            }
        }
    }

    public void CollapseSelected()
    {
        if (SelectedNode == null)
        {
            return;
        }
        if (SelectedNode.IsExpanded)
        {
            SelectedNode.IsExpanded = false;
        }
        else
        {
            // Already collapsed: jump to parent node (ranger-style 'h')
            TreeNodeViewModel? parent = FindParent(SelectedNode);
            if (parent != null)
            {
                SelectNode(parent);
            }
        }
    }

    // Sync tree to current directory

    public async Task NavigateToLocationAsync(Location location)
    {
        TreeNodeViewModel? pcRoot = Roots.FirstOrDefault(n => KnownLocations.IsPc(n.Location));
        if (pcRoot == null)
        {
            return;
        }

        await EnsureExpandedAsync(pcRoot);

        // PC root itself, or a shell-namespace node directly under PC
        // (special folders, Recycle Bin, Network) — match by Location identity.
        if (KnownLocations.IsPc(location))
        {
            SelectNode(pcRoot);
            return;
        }
        if (location.IsShell)
        {
            TreeNodeViewModel? shellNode = pcRoot.Children.FirstOrDefault(c =>
                c.Location.Equals(location)
            );
            if (shellNode != null)
            {
                SelectNode(shellNode);
            }
            return;
        }

        // Filesystem location: descend the physical tree under PC.
        if (!location.TryGetFileSystemPath(out string path))
        {
            return;
        }
        string? driveRoot = Path.GetPathRoot(path);
        if (driveRoot == null)
        {
            return;
        }

        // Collapse drive branches that are not ancestors of the target path.
        foreach (TreeNodeViewModel child in pcRoot.Children)
        {
            CollapseNonAncestors(child, path);
        }

        // The drive node lives one level below the PC root.
        TreeNodeViewModel? rootNode = pcRoot.Children.FirstOrDefault(n =>
            string.Equals(
                n.FullPath.TrimEnd(Path.DirectorySeparatorChar),
                driveRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (rootNode == null)
        {
            return;
        }

        await EnsureExpandedAsync(rootNode);

        TreeNodeViewModel current = rootNode;
        string relativePath = path[driveRoot.Length..];
        string[] parts = relativePath.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries
        );

        foreach (string part in parts)
        {
            TreeNodeViewModel? next = current.Children.FirstOrDefault(c =>
                string.Equals(c.DisplayName, part, StringComparison.OrdinalIgnoreCase)
            );
            if (next == null)
            {
                return;
            }
            current = next;

            if (part != parts[^1])
            {
                await EnsureExpandedAsync(current);
            }
        }

        SelectNode(current);
    }

    // Helpers

    private void SelectNode(TreeNodeViewModel node)
    {
        if (SelectedNode != null)
        {
            SelectedNode.IsSelected = false;
        }
        SelectedNode = node;
        node.IsSelected = true;
    }

    private static async Task EnsureExpandedAsync(TreeNodeViewModel node)
    {
        if (!node.IsExpanded)
        {
            node.IsExpanded = true;
        }
        await node.WaitForChildrenLoadedAsync();
    }

    /// <summary>
    /// Collapse any node that is neither the target path nor an ancestor of it.
    /// Operates recursively so deeply-nested sibling branches are also closed.
    /// </summary>
    private static void CollapseNonAncestors(TreeNodeViewModel node, string targetPath)
    {
        if (node.IsPlaceholder || !node.IsExpanded)
        {
            return;
        }

        string nodePath = node.FullPath.TrimEnd(Path.DirectorySeparatorChar);
        string target = targetPath.TrimEnd(Path.DirectorySeparatorChar);

        bool isAncestorOrSelf =
            string.Equals(nodePath, target, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(
                nodePath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );

        if (!isAncestorOrSelf)
        {
            node.IsExpanded = false;
            return;
        }

        // Node is an ancestor — recurse into its children.
        foreach (TreeNodeViewModel child in node.Children)
        {
            CollapseNonAncestors(child, targetPath);
        }
    }

    /// <summary>Returns the visible (non-placeholder, depth-first) list of nodes.</summary>
    private List<TreeNodeViewModel> GetVisibleNodes()
    {
        List<TreeNodeViewModel> result = [];
        foreach (TreeNodeViewModel root in Roots)
        {
            CollectVisible(root, result);
        }
        return result;
    }

    private static void CollectVisible(TreeNodeViewModel node, List<TreeNodeViewModel> result)
    {
        if (node.IsPlaceholder)
        {
            return;
        }
        result.Add(node);
        if (node.IsExpanded)
        {
            foreach (TreeNodeViewModel child in node.Children)
            {
                CollectVisible(child, result);
            }
        }
    }

    /// <summary>Finds the parent of <paramref name="target"/> in the visible tree.</summary>
    private TreeNodeViewModel? FindParent(TreeNodeViewModel target)
    {
        foreach (TreeNodeViewModel root in Roots)
        {
            TreeNodeViewModel? found = FindParentIn(root, target);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static TreeNodeViewModel? FindParentIn(TreeNodeViewModel node, TreeNodeViewModel target)
    {
        if (node.IsPlaceholder)
        {
            return null;
        }
        foreach (TreeNodeViewModel child in node.Children)
        {
            if (child == target)
            {
                return node;
            }
            TreeNodeViewModel? found = FindParentIn(child, target);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    public void Dispose()
    {
        _pathSubscription.Dispose();
    }
}
