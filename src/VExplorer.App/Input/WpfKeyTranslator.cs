using System.Windows.Input;
using VExplorer.Core.Input;

namespace VExplorer.App.Input;

/// <summary>
/// Translates WPF's <see cref="Key"/> / <see cref="ModifierKeys"/> into the
/// platform-independent <see cref="AppKey"/> / <see cref="AppModifiers"/> the
/// binding table speaks. Numpad digits fold onto D0–D9. Keeps WPF input types out
/// of the binding layer (which lives in Core and is unit-tested).
/// </summary>
public static class WpfKeyTranslator
{
    /// <summary>Maps a WPF key to an <see cref="AppKey"/>; false for keys the app never binds.</summary>
    public static bool TryTranslate(Key key, out AppKey appKey)
    {
        appKey = key switch
        {
            >= Key.A and <= Key.Z => AppKey.A + (key - Key.A),
            >= Key.D0 and <= Key.D9 => AppKey.D0 + (key - Key.D0),
            >= Key.NumPad0 and <= Key.NumPad9 => AppKey.D0 + (key - Key.NumPad0),
            Key.Left => AppKey.Left,
            Key.Right => AppKey.Right,
            Key.Up => AppKey.Up,
            Key.Down => AppKey.Down,
            Key.Return => AppKey.Return,
            Key.Escape => AppKey.Escape,
            Key.Space => AppKey.Space,
            Key.Back => AppKey.Back,
            Key.Delete => AppKey.Delete,
            Key.Tab => AppKey.Tab,
            Key.PageUp => AppKey.PageUp,
            Key.PageDown => AppKey.PageDown,
            Key.F2 => AppKey.F2,
            Key.F4 => AppKey.F4,
            Key.F5 => AppKey.F5,
            Key.OemComma => AppKey.OemComma,
            Key.OemPeriod => AppKey.OemPeriod,
            _ => AppKey.None,
        };
        return appKey != AppKey.None;
    }

    public static AppModifiers Modifiers(ModifierKeys mods)
    {
        AppModifiers result = AppModifiers.None;
        if ((mods & ModifierKeys.Control) != 0)
        {
            result |= AppModifiers.Ctrl;
        }
        if ((mods & ModifierKeys.Shift) != 0)
        {
            result |= AppModifiers.Shift;
        }
        if ((mods & ModifierKeys.Alt) != 0)
        {
            result |= AppModifiers.Alt;
        }
        return result;
    }
}
