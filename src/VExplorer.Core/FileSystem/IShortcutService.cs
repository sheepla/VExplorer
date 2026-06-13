namespace VExplorer.Core.FileSystem;

/// <summary>
/// Creates Windows <c>.lnk</c> shortcuts via the shell (<c>IShellLink</c>),
/// rather than writing the binary format by hand (原則3: OS 委譲).
/// Used by <c>:mkshortcut</c> and <c>:pin start</c> / <c>:pin desktop</c>.
/// </summary>
public interface IShortcutService
{
    /// <summary>
    /// Creates a shortcut at <paramref name="linkPath"/> (a <c>.lnk</c> path)
    /// pointing at <paramref name="targetPath"/>. Returns null on success or a
    /// human-readable error message on failure.
    /// </summary>
    string? Create(string linkPath, string targetPath);
}
