using VExplorer.Core.Actions;
using VExplorer.Core.Modes;

namespace VExplorer.Core.Input;

/// <summary>
/// The hard-coded key/character → <see cref="AppAction"/> table. Replaces the
/// mode/focus/key switch that used to live in the window. Tables are grouped by
/// mode; tab-management chords are checked first because they work in every mode.
/// Focus- and Recycle-Bin-dependent bindings read <see cref="KeyContext"/>; the
/// resulting action stays focus-agnostic and the handler picks the target pane.
/// </summary>
public sealed class KeyBindingMap : IKeyBindingSource
{
    public static KeyBindingMap Default { get; } = new();

    public AppAction? Resolve(KeyContext context, KeyChord chord)
    {
        if (ResolveTabManagement(chord) is { } tab)
        {
            return tab;
        }
        return context.Mode switch
        {
            ModeKind.Normal => ResolveNormal(context, chord),
            ModeKind.Visual => ResolveVisual(chord),
            ModeKind.Menu => ResolveMenu(chord),
            _ => null,
        };
    }

    public AppAction? ResolveText(KeyContext context, string text)
    {
        if (context.Mode != ModeKind.Normal)
        {
            return null;
        }
        return text switch
        {
            ":" => new AppAction.EnterMode(ModeTarget.Command),
            "!" => new AppAction.EnterMode(ModeTarget.Command, "! "),
            "/" => new AppAction.EnterSearch(null),
            "]" => new AppAction.ActivateRelativeTab(1),
            "[" => new AppAction.ActivateRelativeTab(-1),
            "}" => new AppAction.MoveTab(1),
            "{" => new AppAction.MoveTab(-1),
            _ => null,
        };
    }

    /// <summary>Ctrl-based tab management — valid in every mode.</summary>
    private static AppAction? ResolveTabManagement(KeyChord chord)
    {
        if (chord.Second is not null)
        {
            return null;
        }
        return (chord.First, chord.Modifiers) switch
        {
            (AppKey.T, AppModifiers.Ctrl) => new AppAction.NewTab(),
            (AppKey.W, AppModifiers.Ctrl) => new AppAction.CloseActiveTab(),
            (AppKey.Tab, AppModifiers.Ctrl) => new AppAction.ActivateRelativeTab(1),
            (AppKey.Tab, AppModifiers.Ctrl | AppModifiers.Shift) =>
                new AppAction.ActivateRelativeTab(-1),
            _ => null,
        };
    }

    private static AppAction? ResolveNormal(KeyContext ctx, KeyChord chord)
    {
        bool list = ctx.Focus == Focus.List;

        // Two-key chords yy / dd — list focus only.
        if (chord.Second is not null)
        {
            return (chord.First, chord.Second, list) switch
            {
                (AppKey.Y, AppKey.Y, true) => new AppAction.Yank(false),
                (AppKey.D, AppKey.D, true) => new AppAction.Yank(true),
                _ => null,
            };
        }

        AppKey k = chord.First;
        AppModifiers m = chord.Modifiers;

        // Digit keys jump to a tab by position (handler maps 1–8/9/0).
        if (m == AppModifiers.None && TryDigit(k, out int digit))
        {
            return new AppAction.ActivateTabByNumber(digit);
        }

        return (k, m) switch
        {
            // List-only keys.
            (AppKey.N, AppModifiers.None) when list => new AppAction.NextMatch(),
            (AppKey.N, AppModifiers.Shift) when list => new AppAction.PrevMatch(),
            (AppKey.Escape, AppModifiers.None) when list => new AppAction.ClearSelectionOrFilter(),
            (AppKey.Space, AppModifiers.None) when list => new AppAction.ToggleSelectionAtCursor(),
            (AppKey.V, AppModifiers.None) when list => new AppAction.EnterMode(ModeTarget.Visual),
            (AppKey.O, AppModifiers.None) when list => new AppAction.EnterMode(ModeTarget.Menu),
            (AppKey.U, AppModifiers.None) when list => new AppAction.Undo(),
            (AppKey.R, AppModifiers.None) when ctx.IsRecycleBin =>
                new AppAction.RestoreFromRecycleBin(),

            // Cursor / navigation (focus resolved by the handler).
            (AppKey.J, AppModifiers.None) or (AppKey.Down, AppModifiers.None) =>
                new AppAction.MoveCursor(CursorMove.Down),
            (AppKey.K, AppModifiers.None) or (AppKey.Up, AppModifiers.None) =>
                new AppAction.MoveCursor(CursorMove.Up),


            (AppKey.H, AppModifiers.None)
            or
            (AppKey.Left, AppModifiers.None)
            or (AppKey.Back, AppModifiers.None) => new AppAction.NavigateToParent(),
            (AppKey.L, AppModifiers.None) or (AppKey.Right, AppModifiers.None) =>
                new AppAction.NavigateInto(),
            (AppKey.Return, AppModifiers.None) => new AppAction.ActivateItem(),
            (AppKey.G, AppModifiers.None) => new AppAction.MoveCursor(CursorMove.Top),
            (AppKey.G, AppModifiers.Shift) => new AppAction.MoveCursor(CursorMove.Bottom),
            (AppKey.U, AppModifiers.Ctrl) => new AppAction.MoveCursor(CursorMove.HalfPageUp),
            (AppKey.D, AppModifiers.Ctrl) => new AppAction.MoveCursor(CursorMove.HalfPageDown),
            (AppKey.B, AppModifiers.Ctrl) or (AppKey.PageUp, AppModifiers.None) =>
                new AppAction.MoveCursor(CursorMove.PageUp),
            (AppKey.PageDown, AppModifiers.None) => new AppAction.MoveCursor(CursorMove.PageDown),

            // Reload (r outside the bin — inside it r restores; see list-only keys above).
            (AppKey.R, AppModifiers.None) or (AppKey.F5, _) => new AppAction.Reload(),

            // History (back / forward).
            (AppKey.OemComma, AppModifiers.Shift) or (AppKey.Left, AppModifiers.Alt) =>
                new AppAction.GoBack(),
            (AppKey.OemPeriod, AppModifiers.Shift) or (AppKey.Right, AppModifiers.Alt) =>
                new AppAction.GoForward(),

            // Focus.
            (AppKey.H, AppModifiers.Shift) or (AppKey.Left, AppModifiers.Shift) =>
                new AppAction.SetFocus(Focus.Tree),
            (AppKey.L, AppModifiers.Shift) or (AppKey.Right, AppModifiers.Shift) =>
                new AppAction.SetFocus(Focus.List),

            // Address bar.
            (AppKey.L, AppModifiers.Ctrl) or (AppKey.F4, _) => new AppAction.EnterMode(
                ModeTarget.Address
            ),

            // Search / filter.
            (AppKey.F, AppModifiers.Ctrl) => new AppAction.EnterSearch(null),
            (AppKey.F, AppModifiers.Shift) => new AppAction.EnterFilter(null),

            // Clipboard / file operations.
            (AppKey.C, AppModifiers.Ctrl) => new AppAction.Yank(false),
            (AppKey.X, AppModifiers.Ctrl) => new AppAction.Yank(true),
            (AppKey.V, AppModifiers.Ctrl) or (AppKey.P, AppModifiers.None) => new AppAction.Paste(),
            (AppKey.Y, AppModifiers.Shift) => new AppAction.CopyPaths(),
            (AppKey.Z, AppModifiers.Ctrl) => new AppAction.Undo(),
            (AppKey.R, AppModifiers.Ctrl) or (AppKey.Y, AppModifiers.Ctrl) => new AppAction.Redo(),
            (AppKey.X, AppModifiers.None) or (AppKey.Delete, AppModifiers.None) => ctx.IsRecycleBin
                ? new AppAction.DeletePermanent()
                : new AppAction.Trash(),
            (AppKey.Delete, AppModifiers.Shift) => new AppAction.DeletePermanent(),
            (AppKey.F2, _) => new AppAction.BeginRename(),

            _ => null,
        };
    }

    private static AppAction? ResolveVisual(KeyChord chord)
    {
        return (chord.First, chord.Modifiers) switch
        {
            (AppKey.J, AppModifiers.None) or (AppKey.Down, AppModifiers.None) =>
                new AppAction.MoveCursor(CursorMove.Down),
            (AppKey.K, AppModifiers.None) or (AppKey.Up, AppModifiers.None) =>
                new AppAction.MoveCursor(CursorMove.Up),
            (AppKey.G, AppModifiers.None) => new AppAction.MoveCursor(CursorMove.Top),
            (AppKey.G, AppModifiers.Shift) => new AppAction.MoveCursor(CursorMove.Bottom),
            (AppKey.U, AppModifiers.Ctrl) => new AppAction.MoveCursor(CursorMove.HalfPageUp),
            (AppKey.D, AppModifiers.Ctrl) => new AppAction.MoveCursor(CursorMove.HalfPageDown),
            (AppKey.B, AppModifiers.Ctrl) or (AppKey.PageUp, AppModifiers.None) =>
                new AppAction.MoveCursor(CursorMove.PageUp),
            (AppKey.F, AppModifiers.Ctrl) or (AppKey.PageDown, AppModifiers.None) =>
                new AppAction.MoveCursor(CursorMove.PageDown),
            (AppKey.Y, AppModifiers.None) => new AppAction.Yank(false),
            (AppKey.D, AppModifiers.None) => new AppAction.Yank(true),
            (AppKey.X, AppModifiers.None) or (AppKey.Delete, AppModifiers.None) =>
                new AppAction.Trash(),
            (AppKey.Delete, AppModifiers.Shift) => new AppAction.DeletePermanent(),
            (AppKey.Y, AppModifiers.Shift) => new AppAction.CopyPaths(),
            (AppKey.Return, _) => new AppAction.ExitToNormal(),
            (AppKey.Escape, _) => new AppAction.ClearSelectionOrFilter(),
            _ => null,
        };
    }

    private static AppAction? ResolveMenu(KeyChord chord)
    {
        AppKey k = chord.First;
        AppModifiers m = chord.Modifiers;
        if (m == AppModifiers.Ctrl && k == AppKey.D)
        {
            return new AppAction.MenuMove(CursorMove.PageDown);
        }
        if (m == AppModifiers.Ctrl && k == AppKey.U)
        {
            return new AppAction.MenuMove(CursorMove.PageUp);
        }
        if (m == AppModifiers.Shift && k == AppKey.G)
        {
            return new AppAction.MenuMove(CursorMove.Bottom);
        }
        return k switch
        {
            AppKey.J or AppKey.Down => new AppAction.MenuMove(CursorMove.Down),
            AppKey.K or AppKey.Up => new AppAction.MenuMove(CursorMove.Up),
            AppKey.G => new AppAction.MenuMove(CursorMove.Top),
            AppKey.L or AppKey.Right => new AppAction.MenuEnterSubmenu(),
            AppKey.H or AppKey.Left => new AppAction.MenuBack(),
            AppKey.Return => new AppAction.MenuInvoke(),
            AppKey.Escape => new AppAction.MenuClose(),
            _ => null,
        };
    }

    private static bool TryDigit(AppKey key, out int digit)
    {
        if (key is >= AppKey.D0 and <= AppKey.D9)
        {
            digit = key - AppKey.D0;
            return true;
        }
        digit = 0;
        return false;
    }
}
