using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using VExplorer.Core.FileSystem;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Windows implementation of <see cref="IPathCompletionSource"/>. Enumerates a
/// directory's immediate children including hidden/system entries (completion is
/// independent of the <c>:set hidden</c> display setting). Enumeration runs on a
/// background thread and honours cancellation, mirroring
/// <see cref="WindowsDirectoryLister"/>.
/// </summary>
public sealed class WindowsPathCompletionSource(ILogger<WindowsPathCompletionSource> logger)
    : IPathCompletionSource
{
    private readonly ILogger<WindowsPathCompletionSource> _logger = logger;

    public async IAsyncEnumerable<PathEntry> EnumerateAsync(
        string directoryPath,
        [EnumeratorCancellation] CancellationToken cancel
    )
    {
        // Snapshot synchronously on a worker thread; large/slow directories must
        // not block the UI.
        List<PathEntry> entries = await Task.Run(
            () => EnumerateSync(directoryPath, cancel),
            cancel
        );
        foreach (PathEntry entry in entries)
        {
            yield return entry;
        }
    }

    private List<PathEntry> EnumerateSync(string directoryPath, CancellationToken cancel)
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
        catch (Exception ex)
            when (ex
                    is UnauthorizedAccessException
                        or DirectoryNotFoundException
                        or IOException
                        or ArgumentException
            )
        {
            // Expected while typing a path (missing/inaccessible directory): yield
            // what we have without surfacing anything to the UI.
            _logger.LogDebug(ex, "Completion enumeration failed for {Directory}", directoryPath);
        }

        return entries;
    }
}
