namespace VExplorer.Core.FileSystem;

/// <summary>
/// A single child entry returned while enumerating a directory for completion.
/// Unlike <see cref="FileItem"/> this carries only what the completion popup
/// needs (name + folder flag); sizes and timestamps are deliberately omitted.
/// </summary>
public readonly record struct PathEntry(string Name, bool IsDirectory);

/// <summary>
/// Enumerates the immediate children of a directory for path completion.
/// <para>
/// Unlike <see cref="IDirectoryLister"/>, this source <b>always</b> yields
/// hidden and system entries: completion is an active intent to go somewhere,
/// so a target should be reachable even when it is not visible in the list
/// (see <c>VExplorer_Completion.md</c>).
/// </para>
/// </summary>
public interface IPathCompletionSource
{
    IAsyncEnumerable<PathEntry> EnumerateAsync(string directoryPath, CancellationToken cancel);
}
