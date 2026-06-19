namespace VExplorer.Core.State;

/// <summary>
/// UI colour theme. <see cref="System"/> follows the Windows app theme
/// (light/dark) at startup; the others pin an explicit appearance.
/// </summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}
