namespace VExplorer.Core.FileSystem;

/// <summary>A network share exposed by a server.</summary>
/// <param name="Name">The share name (e.g. <c>Public</c>).</param>
/// <param name="UncPath">The full UNC path to the share (e.g. <c>\\server\Public</c>).</param>
public readonly record struct UncShareEntry(string Name, string UncPath);

/// <summary>
/// Enumerates the shares a server exposes. Like network discovery this is slow
/// and may fail (offline / timeout / access); callers run it off the UI thread
/// and surface failures as an empty list.
/// </summary>
public interface IUncShareSource
{
    IReadOnlyList<UncShareEntry> ListShares(string server);
}
