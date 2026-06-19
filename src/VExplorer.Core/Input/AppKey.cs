namespace VExplorer.Core.Input;

/// <summary>
/// A keyboard key, decoupled from WPF's <c>System.Windows.Input.Key</c> so the
/// binding table lives in Core and stays unit-testable. The App layer translates
/// the platform key into this (see <c>WpfKeyTranslator</c>). Only keys the app
/// actually binds are listed; numpad digits map onto D0–D9 at translation time.
/// </summary>
public enum AppKey
{
    None,

    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,

    D0,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    D7,
    D8,
    D9,

    Left,
    Right,
    Up,
    Down,

    Return,
    Escape,
    Space,
    Back,
    Delete,
    Tab,
    PageUp,
    PageDown,

    F2,
    F4,
    F5,

    OemComma,
    OemPeriod,
}

/// <summary>Active modifier keys. Matches WPF's <c>ModifierKeys</c> values.</summary>
[Flags]
public enum AppModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}
