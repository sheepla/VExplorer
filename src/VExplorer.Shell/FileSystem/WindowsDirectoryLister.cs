using VExplorer.Core.Completion;
using VExplorer.Core.FileSystem;
using VExplorer.Core.State;

namespace VExplorer.Shell.FileSystem;

public sealed class WindowsDirectoryLister(AppState appState) : IDirectoryLister
{
    private readonly AppState _appState = appState;

    public ValueTask<IFileItemSource> ListAsync(
        Location location,
        ListOptions options,
        CancellationToken cancel
    )
    {
        // The physical lister only handles filesystem locations; shell-namespace
        // locations are dispatched by VirtualDirectoryLister before reaching here.
        if (!location.TryGetFileSystemPath(out string path))
        {
            return new ValueTask<IFileItemSource>(InMemoryFileItemSource.Empty);
        }
        return new ValueTask<IFileItemSource>(
            Task.Run<IFileItemSource>(() => ListSync(path, options, cancel), cancel)
        );
    }

    private IFileItemSource ListSync(string path, ListOptions options, CancellationToken cancel)
    {
        // Read settings fresh each listing so a live :set (hidden/systemfiles/
        // foldersfirst/fuzzy) takes effect on the next refresh.
        Settings snap = _appState.Settings;
        string? filter = string.IsNullOrEmpty(options.NameFilter) ? null : options.NameFilter;

        // Enumerate via DirectoryInfo.EnumerateFileSystemInfos: each FileSystemInfo is
        // populated from the single OS directory scan (attributes/size/time come from
        // the find-data), so there is no per-entry FileInfo re-stat — the main cost on
        // folders like winsxs. (We avoid the lower-level FileSystemEnumerable here: its
        // ref-struct FileSystemEntry callback generates metadata the WPF markup
        // compiler's type loader cannot resolve, which breaks XAML compilation.)
        EnumerationOptions enumOptions = new()
        {
            IgnoreInaccessible = true,
            // We apply Hidden/System filtering ourselves (settings-driven), so disable
            // the default skip which would hide them unconditionally.
            AttributesToSkip = 0,
            RecurseSubdirectories = false,
        };

        // A time budget caps how long the UI stays blank on a huge folder: when it
        // elapses we return what we have so far (marked truncated). 0 = unlimited.
        using CancellationTokenSource? timeoutCts =
            options.TimeoutMs > 0 ? new CancellationTokenSource(options.TimeoutMs) : null;

        List<FileItem> items = [];
        bool truncated = false;
        try
        {
            DirectoryInfo dir = new(path);
            using IEnumerator<FileSystemInfo> e = dir.EnumerateFileSystemInfos("*", enumOptions)
                .GetEnumerator();
            while (true)
            {
                // Navigation supersede → discard the whole load (propagate).
                cancel.ThrowIfCancellationRequested();
                // Time budget elapsed → keep the partial result, stop scanning.
                if (timeoutCts is { IsCancellationRequested: true })
                {
                    truncated = true;
                    break;
                }
                if (!e.MoveNext())
                {
                    break;
                }

                FileSystemInfo info = e.Current;
                FileAttributes attrs = info.Attributes;
                bool isDir = (attrs & FileAttributes.Directory) != 0;

                if (!snap.ShowHidden && (attrs & FileAttributes.Hidden) != 0)
                {
                    continue;
                }
                if (!snap.ShowSystemFiles && (attrs & FileAttributes.System) != 0)
                {
                    continue;
                }
                // Pre-filter so non-matching entries are never materialized.
                if (filter != null && !CompletionMatcher.IsMatch(info.Name, filter, snap.Fuzzy))
                {
                    continue;
                }

                items.Add(
                    new FileItem
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        IsDirectory = isDir,
                        SizeBytes = isDir ? null : (info as FileInfo)?.Length,
                        LastWriteTimeUtc = info.LastWriteTimeUtc,
                        Extension = isDir ? "" : info.Extension,
                        IsHidden = (attrs & FileAttributes.Hidden) != 0,
                        IsSystem = (attrs & FileAttributes.System) != 0,
                    }
                );
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }

        if (snap.FoldersFirst)
        {
            items.Sort(
                (FileItem a, FileItem b) =>
                {
                    if (a.IsDirectory != b.IsDirectory)
                    {
                        return a.IsDirectory ? -1 : 1;
                    }
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                }
            );
        }
        else
        {
            items.Sort(
                (FileItem a, FileItem b) =>
                    string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            );
        }

        return new InMemoryFileItemSource(items, truncated);
    }
}
