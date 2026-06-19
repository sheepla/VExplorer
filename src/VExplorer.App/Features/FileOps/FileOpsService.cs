using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using VExplorer.App.Diagnostics;
using VExplorer.App.Features.FileList;
using VExplorer.Core.FileSystem;
using VExplorer.Core.State;

namespace VExplorer.App.Features.FileOps;

/// <summary>
/// Central entry point for file operations (writer pattern): read the
/// targets from central state, delegate the side-effect to the shell, reflect
/// the result back. Singleton — the scoped <see cref="TabState"/> /
/// <see cref="FileListViewModel"/> and the owner HWND are passed as arguments.
/// The OS provides progress/conflict/confirmation UI via <see cref="IShellFileOps"/>.
/// <para>
/// On success, reversible operations push an entry to <see cref="IOperationHistory"/>
/// so they can be undone / redone (u / Ctrl+Z, Ctrl+R / Ctrl+Y). Permanent delete
/// and empty-recycle-bin are not reversible and are never recorded.
/// </para>
/// </summary>
public sealed class FileOpsService(
    IShellFileOps shell,
    IOperationHistory history,
    IRecycleBinSource recycleBin,
    TabManager tabManager,
    ErrorReporter errors,
    ILogger<FileOpsService> logger
)
{
    private readonly IShellFileOps _shell = shell;
    private readonly IOperationHistory _history = history;
    private readonly IRecycleBinSource _recycleBin = recycleBin;
    private readonly TabManager _tabManager = tabManager;
    private readonly ErrorReporter _errors = errors;
    private readonly ILogger<FileOpsService> _logger = logger;

    private static nint OwnerHwnd =>
        Application.Current?.MainWindow is { } w ? new WindowInteropHelper(w).Handle : 0;

    public async Task TrashAsync(FileListViewModel list, TabState tab, nint hwnd)
    {
        IReadOnlyList<string> targets = list.ResolveTargetPaths();
        if (targets.Count == 0)
        {
            return;
        }
        ShellOpResult r = await _shell.DeleteAsync(targets, recycle: true, hwnd);
        Reflect(tab, r, $"Moved {Describe(targets)} to Recycle Bin");
        if (r.IsSuccess)
        {
            PushTrash(targets);
        }
    }

    public async Task DeletePermanentAsync(FileListViewModel list, TabState tab, nint hwnd)
    {
        IReadOnlyList<string> targets = list.ResolveTargetPaths();
        if (targets.Count == 0)
        {
            return;
        }
        // Permanent delete is not reversible — never recorded in history.
        ShellOpResult r = await _shell.DeleteAsync(targets, recycle: false, hwnd);
        Reflect(tab, r, $"Deleted {Describe(targets)}");
    }

    public async Task RenameAsync(string sourcePath, string newName, TabState tab, nint hwnd)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }
        ShellOpResult r = await _shell.RenameAsync(sourcePath, newName, hwnd);
        Reflect(tab, r, $"Renamed to {newName}", focusName: newName);
        if (r.IsSuccess)
        {
            PushRename(sourcePath, newName);
        }
    }

    public async Task MkdirAsync(TabState tab, string name, nint hwnd)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        if (tab.CurrentDirectoryPath is not string dir)
        {
            tab.SetStatusMessage("No destination folder here");
            return;
        }
        ShellOpResult r = await _shell.NewFolderAsync(dir, name, hwnd);
        Reflect(tab, r, $"Created folder \"{name}\"", focusName: name);
        if (r.IsSuccess)
        {
            PushMkdir(dir, name);
        }
    }

    /// <summary>
    /// <c>:touch FILE</c> — create an empty file. The parent directory must already
    /// exist. An existing file/folder of the same name is an error (no overwrite).
    /// </summary>
    public Task TouchAsync(TabState tab, string fullPath, nint hwnd)
    {
        return CreateEmptyFileAsync(tab, fullPath, hwnd, makeParents: false);
    }

    /// <summary>
    /// <c>:newfile FILE</c> — create an empty file, creating intermediate parent
    /// directories as needed (<c>mkdir -p</c> + <c>touch</c>). An existing file is an error.
    /// </summary>
    public Task NewFileAsync(TabState tab, string fullPath, nint hwnd)
    {
        return CreateEmptyFileAsync(tab, fullPath, hwnd, makeParents: true);
    }

    private async Task CreateEmptyFileAsync(
        TabState tab,
        string fullPath,
        nint hwnd,
        bool makeParents
    )
    {
        string path = fullPath.TrimEnd(Path.DirectorySeparatorChar);
        string parent = Path.GetDirectoryName(path) ?? "";
        string name = Path.GetFileName(path);
        if (name.Length == 0 || parent.Length == 0)
        {
            tab.SetStatusMessage("usage: provide a file path");
            return;
        }
        // Existing file/folder → error and do nothing (no implicit overwrite).
        if (File.Exists(path) || Directory.Exists(path))
        {
            tab.SetStatusMessage($"Already exists: {name}");
            return;
        }
        if (!Directory.Exists(parent))
        {
            if (!makeParents)
            {
                tab.SetStatusMessage($"No such directory: {parent}");
                return;
            }
            try
            {
                Directory.CreateDirectory(parent);
            }
            catch (Exception ex)
            {
                _errors.Report(tab, "Create parent directory", ex, ("Parent", parent));
                return;
            }
        }
        ShellOpResult r = await _shell.NewFileAsync(parent, name, hwnd);
        Reflect(tab, r, $"Created file \"{name}\"", focusName: name);
        if (r.IsSuccess)
        {
            PushNewFile(parent, name);
        }
    }

    /// <summary>yy / Ctrl+C (copy) and Ctrl+X (cut) — place targets on the clipboard.</summary>
    public void YankToClipboard(FileListViewModel list, TabState tab, bool cut)
    {
        IReadOnlyList<string> targets = list.ResolveTargetPaths();
        if (targets.Count == 0)
        {
            return;
        }
        ShellClipboard.SetPaths(targets, cut);
        tab.SetStatusMessage($"{(cut ? "Cut" : "Yanked")} {Describe(targets)}", isError: false);
    }

    /// <summary>Y — copy the target full paths to the clipboard as text.</summary>
    public void CopyPathsAsText(FileListViewModel list, TabState tab)
    {
        IReadOnlyList<string> targets = list.ResolveTargetPaths();
        if (targets.Count == 0)
        {
            return;
        }
        ShellClipboard.SetText(string.Join(Environment.NewLine, targets));
        tab.SetStatusMessage($"Copied path of {Describe(targets)}", isError: false);
    }

    /// <summary>p / Ctrl+V — paste clipboard files into the current directory.</summary>
    public async Task PasteAsync(TabState tab, nint hwnd)
    {
        (IReadOnlyList<string> paths, bool isCut) = ShellClipboard.GetPaths();
        if (paths.Count == 0)
        {
            return;
        }
        // Putting into the Recycle Bin = sending the items to the Recycle Bin.
        if (KnownLocations.IsRecycleBin(tab.CurrentLocationValue))
        {
            ShellOpResult del = await _shell.DeleteAsync(paths, recycle: true, hwnd);
            if (del.IsSuccess)
            {
                ShellClipboard.Clear();
            }
            Reflect(tab, del, $"Moved {Describe(paths)} to Recycle Bin");
            if (del.IsSuccess)
            {
                PushTrash(paths);
            }
            return;
        }
        if (tab.CurrentDirectoryPath is not string dest)
        {
            tab.SetStatusMessage("No destination folder here");
            return;
        }

        if (isCut)
        {
            // Cutting and pasting into the same folder is a no-op.
            if (AllInDirectory(paths, dest))
            {
                tab.SetStatusMessage("Already in this folder", isError: false);
                return;
            }
            ShellOpResult mv = await _shell.MoveAsync(paths, dest, hwnd);
            if (mv.IsSuccess)
            {
                ShellClipboard.Clear();
            }
            Reflect(tab, mv, $"Moved {Describe(paths)}", focusName: FirstLeaf(paths));
            if (mv.IsSuccess)
            {
                PushMove(paths, dest);
            }
        }
        else
        {
            // Copying into a source's own folder → auto-rename ("file (2)").
            bool inPlace = AnyInDirectory(paths, dest);
            HashSet<string> before = SnapshotEntries(dest);
            ShellOpResult cp = await _shell.CopyAsync(paths, dest, hwnd, autoRename: inPlace);
            List<string> created = cp.IsSuccess ? CreatedSince(dest, before) : [];
            Reflect(tab, cp, $"Copied {Describe(paths)}", focusName: FirstLeaf(created));
            if (cp.IsSuccess)
            {
                PushCopy(paths, dest, inPlace, created);
            }
        }
    }

    /// <summary>Drag &amp; drop: copy (Ctrl) or move dropped paths into the target dir.</summary>
    public async Task DropAsync(
        IReadOnlyList<string> paths,
        string targetDirectory,
        bool copy,
        nint hwnd,
        TabState tab
    )
    {
        if (paths.Count == 0)
        {
            return;
        }
        if (copy)
        {
            bool inPlace = AnyInDirectory(paths, targetDirectory);
            HashSet<string> before = SnapshotEntries(targetDirectory);
            ShellOpResult r = await _shell.CopyAsync(
                paths,
                targetDirectory,
                hwnd,
                autoRename: inPlace
            );
            List<string> created = r.IsSuccess ? CreatedSince(targetDirectory, before) : [];
            Reflect(tab, r, $"Copied {Describe(paths)}", focusName: FirstLeaf(created));
            if (r.IsSuccess)
            {
                PushCopy(paths, targetDirectory, inPlace, created);
            }
        }
        else
        {
            ShellOpResult r = await _shell.MoveAsync(paths, targetDirectory, hwnd);
            Reflect(tab, r, $"Moved {Describe(paths)}", focusName: FirstLeaf(paths));
            if (r.IsSuccess)
            {
                PushMove(paths, targetDirectory);
            }
        }
    }

    /// <summary>Copy <paramref name="source"/> to a full destination path (Unix <c>cp SRC DEST</c>).</summary>
    public async Task CopyToPathAsync(string source, string destFullPath, TabState tab, nint hwnd)
    {
        if (!TrySplitDest(destFullPath, out string parent, out string newName))
        {
            tab.SetStatusMessage($"No such directory: {Path.GetDirectoryName(destFullPath)}");
            return;
        }
        ShellOpResult r = await _shell.CopyRenameAsync(source, parent, newName, hwnd);
        Reflect(tab, r, $"Copied to \"{newName}\"", focusName: newName);
        if (r.IsSuccess)
        {
            PushCopyTo(source, parent, newName);
        }
    }

    /// <summary>Move <paramref name="source"/> to a full destination path (Unix <c>mv SRC DEST</c>).</summary>
    public async Task MoveToPathAsync(string source, string destFullPath, TabState tab, nint hwnd)
    {
        if (!TrySplitDest(destFullPath, out string parent, out string newName))
        {
            tab.SetStatusMessage($"No such directory: {Path.GetDirectoryName(destFullPath)}");
            return;
        }
        string trimmedSrc = source.TrimEnd(Path.DirectorySeparatorChar);
        string srcParent = Path.GetDirectoryName(trimmedSrc) ?? "";
        string srcLeaf = Path.GetFileName(trimmedSrc);
        ShellOpResult r = await _shell.MoveRenameAsync(source, parent, newName, hwnd);
        Reflect(tab, r, $"Moved to \"{newName}\"", focusName: newName);
        if (r.IsSuccess)
        {
            PushMoveTo(source, srcParent, srcLeaf, parent, newName);
        }
    }

    /// <summary>Splits a full destination path into an existing parent dir and a leaf name.</summary>
    private static bool TrySplitDest(string destFullPath, out string parent, out string newName)
    {
        string dest = destFullPath.TrimEnd(Path.DirectorySeparatorChar);
        parent = Path.GetDirectoryName(dest) ?? "";
        newName = Path.GetFileName(dest);
        return newName.Length > 0 && parent.Length > 0 && Directory.Exists(parent);
    }

    // Undo/redo recording

    private void PushCopyTo(string source, string destParent, string newName)
    {
        string created = Path.Combine(destParent, newName);
        _history.Push(
            new OperationEntry(
                "copy",
                Undo: async _ =>
                    ReflectInverse(
                        await _shell.DeleteAsync([created], recycle: true, OwnerHwnd),
                        $"Undid copy \"{newName}\""
                    ),
                Redo: async _ =>
                    ReflectInverse(
                        await _shell.CopyRenameAsync(source, destParent, newName, OwnerHwnd),
                        $"Redid copy \"{newName}\"",
                        focusName: newName
                    )
            )
        );
    }

    private void PushMoveTo(
        string source,
        string srcParent,
        string srcLeaf,
        string destParent,
        string newName
    )
    {
        string moved = Path.Combine(destParent, newName);
        _history.Push(
            new OperationEntry(
                "move",
                Undo: async _ =>
                    ReflectInverse(
                        await _shell.MoveRenameAsync(moved, srcParent, srcLeaf, OwnerHwnd),
                        $"Undid move \"{srcLeaf}\"",
                        focusName: srcLeaf
                    ),
                Redo: async _ =>
                    ReflectInverse(
                        await _shell.MoveRenameAsync(source, destParent, newName, OwnerHwnd),
                        $"Redid move \"{newName}\"",
                        focusName: newName
                    )
            )
        );
    }

    private void PushTrash(IReadOnlyList<string> originalPaths)
    {
        string[] paths = [.. originalPaths];
        string what = Describe(paths);
        _history.Push(
            new OperationEntry(
                "delete",
                Undo: _ =>
                {
                    bool ok = RestoreFromBin(paths);
                    return Task.FromResult(
                        ok
                            ? Finish(true, $"Restored {what}", focusName: FirstLeaf(paths))
                            : Finish(false, "", "Nothing to restore")
                    );
                },
                Redo: async _ =>
                    ReflectInverse(
                        await _shell.DeleteAsync(paths, recycle: true, OwnerHwnd),
                        $"Redid delete {what}"
                    )
            )
        );
    }

    private void PushRename(string sourcePath, string newName)
    {
        (string newPath, string oldName) = FileOpInverses.DeriveRenameInverse(sourcePath, newName);
        _history.Push(
            new OperationEntry(
                "rename",
                Undo: async _ =>
                    ReflectInverse(
                        await _shell.RenameAsync(newPath, oldName, OwnerHwnd),
                        $"Undid rename → \"{oldName}\"",
                        focusName: oldName
                    ),
                Redo: async _ =>
                    ReflectInverse(
                        await _shell.RenameAsync(sourcePath, newName, OwnerHwnd),
                        $"Redid rename → \"{newName}\"",
                        focusName: newName
                    )
            )
        );
    }

    private void PushMkdir(string directory, string name)
    {
        string created = FileOpInverses.DeriveCreatedFolderPath(directory, name);
        _history.Push(
            new OperationEntry(
                "new folder",
                Undo: async _ =>
                    ReflectInverse(
                        await _shell.DeleteAsync([created], recycle: true, OwnerHwnd),
                        $"Undid new folder \"{name}\""
                    ),
                Redo: async _ =>
                    ReflectInverse(
                        await _shell.NewFolderAsync(directory, name, OwnerHwnd),
                        $"Redid new folder \"{name}\"",
                        focusName: name
                    )
            )
        );
    }

    private void PushNewFile(string directory, string name)
    {
        string created = Path.Combine(directory, name);
        _history.Push(
            new OperationEntry(
                "new file",
                Undo: async _ =>
                    ReflectInverse(
                        await _shell.DeleteAsync([created], recycle: true, OwnerHwnd),
                        $"Undid new file \"{name}\""
                    ),
                Redo: async _ =>
                    ReflectInverse(
                        await _shell.NewFileAsync(directory, name, OwnerHwnd),
                        $"Redid new file \"{name}\"",
                        focusName: name
                    )
            )
        );
    }

    private void PushMove(IReadOnlyList<string> sources, string destDir)
    {
        string[] srcs = [.. sources];
        string what = Describe(srcs);
        IReadOnlyList<(string SourceParent, IReadOnlyList<string> DestPaths)> groups =
            FileOpInverses.DeriveMovedDestPaths(srcs, destDir);
        _history.Push(
            new OperationEntry(
                "move",
                Undo: async _ =>
                {
                    bool all = true;
                    foreach ((string parent, IReadOnlyList<string> dests) in groups)
                    {
                        ShellOpResult r = await _shell.MoveAsync(dests, parent, OwnerHwnd);
                        all &= r.IsSuccess;
                    }
                    return Finish(all, $"Undid move {what}", focusName: FirstLeaf(srcs));
                },
                Redo: async _ =>
                    ReflectInverse(
                        await _shell.MoveAsync(srcs, destDir, OwnerHwnd),
                        $"Redid move {what}"
                    )
            )
        );
    }

    private void PushCopy(
        IReadOnlyList<string> sources,
        string destDir,
        bool autoRename,
        List<string> created
    )
    {
        string[] srcs = [.. sources];
        string what = Describe(srcs);
        _history.Push(
            new OperationEntry(
                "copy",
                Undo: async _ =>
                {
                    if (created.Count == 0)
                    {
                        return Finish(false, "", "Nothing to undo");
                    }
                    return ReflectInverse(
                        await _shell.DeleteAsync(created, recycle: true, OwnerHwnd),
                        $"Undid copy {what}"
                    );
                },
                Redo: async _ =>
                {
                    HashSet<string> before = SnapshotEntries(destDir);
                    ShellOpResult r = await _shell.CopyAsync(srcs, destDir, OwnerHwnd, autoRename);
                    if (r.IsSuccess)
                    {
                        // Refresh the created set so a following undo targets the new copies.
                        created.Clear();
                        created.AddRange(CreatedSince(destDir, before));
                    }
                    return ReflectInverse(r, $"Redid copy {what}", focusName: FirstLeaf(created));
                }
            )
        );
    }

    /// <summary>Restores items from the Recycle Bin whose original full path is in <paramref name="originalPaths"/>.</summary>
    private bool RestoreFromBin(IReadOnlyList<string> originalPaths)
    {
        HashSet<string> wanted = new(
            originalPaths.Select(p => p.TrimEnd(Path.DirectorySeparatorChar)),
            StringComparer.OrdinalIgnoreCase
        );

        List<int> tokens = _recycleBin
            .List()
            .Where(i =>
                wanted.Contains(
                    Path.Combine(i.OriginalPath, i.DisplayName).TrimEnd(Path.DirectorySeparatorChar)
                )
            )
            .GroupBy(
                i => Path.Combine(i.OriginalPath, i.DisplayName),
                StringComparer.OrdinalIgnoreCase
            )
            .Select(g => g.OrderByDescending(x => x.DeletedUtc ?? DateTime.MinValue).First().Token)
            .ToList();

        if (tokens.Count == 0)
        {
            return false;
        }
        _recycleBin.Restore(tokens);
        return true;
    }

    /// <summary>Top-level entries of <paramref name="directory"/>, or empty when unreadable.</summary>
    private HashSet<string> SnapshotEntries(string directory)
    {
        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                set.Add(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable destination → empty snapshot (undo simply finds nothing).
            _logger.LogDebug(ex, "Could not snapshot entries of {Directory}", directory);
        }
        return set;
    }

    private List<string> CreatedSince(string directory, HashSet<string> before)
    {
        return SnapshotEntries(directory).Where(e => !before.Contains(e)).ToList();
    }

    /// <summary>Runs an inverse/redo result against the active tab and reports it.</summary>
    private bool ReflectInverse(ShellOpResult r, string okMessage, string? focusName = null)
    {
        return Finish(r.IsSuccess, okMessage, r.ErrorMessage, focusName);
    }

    private bool Finish(
        bool success,
        string okMessage,
        string? errorMessage = null,
        string? focusName = null
    )
    {
        TabState tab = _tabManager.GetActiveTabState();
        if (success)
        {
            tab.RequestRefresh(focusName);
            tab.SetStatusMessage(okMessage, isError: false);
        }
        else
        {
            tab.SetStatusMessage(errorMessage ?? "Operation failed");
        }
        return success;
    }

    /// <summary>The leaf name of the first path, for post-op cursor focus; null when empty.</summary>
    private static string? FirstLeaf(IReadOnlyList<string> paths)
    {
        return paths.Count > 0
            ? Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar))
            : null;
    }

    /// <summary>"name" for a single item, "name" (+N more) for several.</summary>
    private static string Describe(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return "0 items";
        }
        string first = Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar));
        return paths.Count == 1 ? $"\"{first}\"" : $"\"{first}\" (+{paths.Count - 1} more)";
    }

    /// <summary>True when every path's parent directory equals <paramref name="directory"/>.</summary>
    private static bool AllInDirectory(IReadOnlyList<string> paths, string directory)
    {
        return paths.Count > 0 && paths.All(p => IsParent(directory, p));
    }

    /// <summary>True when any path's parent directory equals <paramref name="directory"/>.</summary>
    private static bool AnyInDirectory(IReadOnlyList<string> paths, string directory)
    {
        return paths.Any(p => IsParent(directory, p));
    }

    private static bool IsParent(string directory, string childPath)
    {
        string? parent = Path.GetDirectoryName(childPath.TrimEnd(Path.DirectorySeparatorChar));
        return parent != null
            && string.Equals(
                parent.TrimEnd(Path.DirectorySeparatorChar),
                directory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static void Reflect(
        TabState tab,
        ShellOpResult result,
        string successMessage,
        string? focusName = null
    )
    {
        switch (result.Status)
        {
            case ShellOpStatus.Success:
                tab.RequestRefresh(focusName);
                tab.SetStatusMessage(successMessage, isError: false);
                break;
            case ShellOpStatus.Cancelled:
                // Quiet — the user dismissed the OS dialog.
                break;
            case ShellOpStatus.Error:
                tab.SetStatusMessage(result.ErrorMessage ?? "Operation failed", isError: true);
                break;
        }
    }
}
