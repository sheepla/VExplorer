namespace VExplorer.Core.FileSystem;

/// <summary>
/// Supplies the name of the currently focused item, used by <c>:rename</c>
/// completion so Tab pre-fills the existing name for partial editing.
/// </summary>
public interface ICurrentItemSource
{
    /// <summary>The cursor item's name, or null when there is none (or it is "..").</summary>
    string? GetCurrentItemName();
}
