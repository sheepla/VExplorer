namespace VExplorer.Core.FileSystem;

/// <summary>
/// Thin wrappers over shell UI that VExplorer delegates to rather than
/// re-implementing: the standard Properties dialog and the
/// "Open with" picker. Implemented in the Shell layer.
/// </summary>
public interface IShellIntegration
{
    /// <summary>
    /// Opens the shell Properties dialog for <paramref name="path"/>.
    /// <paramref name="ownerHwnd"/> parents the dialog (0 for none).
    /// </summary>
    bool ShowProperties(string path, nint ownerHwnd);

    /// <summary>Opens the "Open with" picker for <paramref name="path"/>.</summary>
    bool ShowOpenWith(string path, nint ownerHwnd);
}
