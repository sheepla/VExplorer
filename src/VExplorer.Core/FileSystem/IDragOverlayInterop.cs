namespace VExplorer.Core.FileSystem;

/// <summary>
/// Native window helpers for the drag-count overlay: reading the live cursor
/// position during a drag loop and making a top-level window hit-test
/// transparent so it never intercepts the drop target's drag events.
/// </summary>
public interface IDragOverlayInterop
{
    /// <summary>
    /// Reads the current cursor position in physical screen pixels.
    /// Returns false when the position could not be retrieved.
    /// </summary>
    bool TryGetCursorPosition(out int x, out int y);

    /// <summary>
    /// Marks the window as layered and hit-test transparent so mouse messages
    /// pass through to the windows beneath it (keeping drop targets reachable).
    /// </summary>
    void MakeHitTestTransparent(nint hwnd);
}
