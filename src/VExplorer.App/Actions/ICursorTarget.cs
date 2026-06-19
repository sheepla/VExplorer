namespace VExplorer.App.Actions;

/// <summary>
/// The cursor-movement surface shared by the file list and the tree, so a
/// <c>MoveCursor</c> action can target whichever pane has focus without the
/// dispatcher branching on the concrete view model.
/// </summary>
public interface ICursorTarget
{
    void MoveCursorDown();
    void MoveCursorUp();
    void MoveCursorToTop();
    void MoveCursorToBottom();
    void MoveCursorPageUp(int step);
    void MoveCursorPageDown(int step);
}
