using System.Runtime.CompilerServices;
using VExplorer.Core.FileSystem;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Windows implementation of <see cref="IPathCompletionSource"/>. Enumerates a
/// directory's immediate children including hidden/system entries (completion is
/// independent of the <c>:set hidden</c> display setting). Enumeration runs on a
/// background thread and honours cancellation, mirroring
/// <see cref="WindowsDirectoryLister"/>.
/// </summary>
public sealed class WindowsPathCompletionSource : IPathCompletionSource
{
    public async IAsyncEnumerable<PathEntry> EnumerateAsync(
        string directoryPath,
        [EnumeratorCancellation] CancellationToken cancel
    )
    {
        // Snapshot synchronously on a worker thread; large/slow directories must
        // not block the UI. Errors (missing dir, access denied) yield nothing.
        List<PathEntry> entries = await Task.Run(
            () => EnumerateSync(directoryPath, cancel),
            cancel
        );
        foreach (PathEntry entry in entries)
        {
            yield return entry;
        }
    }

    private static List<PathEntry> EnumerateSync(string directoryPath, CancellationToken cancel)
    {
        List<PathEntry> entries = [];
        try
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directoryPath))
            {
                cancel.ThrowIfCancellationRequested();
                bool isDir = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
                entries.Add(new PathEntry(Path.GetFileName(path), isDir));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (ArgumentException) { }

        return entries;
    }
}
