namespace VExplorer.Core.FileSystem;

/// <summary>A machine (or share) discovered under the Network shell folder.</summary>
public readonly record struct NetworkEntry(string DisplayName, string UncPath, bool IsContainer);

/// <summary>
/// Enumerates the Windows Network shell folder (LAN machines). Enumeration is
/// inherently slow and may fail (offline / timeout); callers run it off the UI
/// thread and surface failures as an empty list.
/// </summary>
public interface INetworkSource
{
    IReadOnlyList<NetworkEntry> List();
}
