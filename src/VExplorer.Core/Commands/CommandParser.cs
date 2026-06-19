using VExplorer.Core.Actions;

namespace VExplorer.Core.Commands;

/// <summary>
/// Parses a COMMAND-mode command line into an <see cref="AppAction"/>. This is the
/// only command-specific parsing in the app; argument resolution (path resolving,
/// existence checks) stays in the handler. Returns null for an empty line or an
/// unknown command name (the caller reports the latter).
/// </summary>
public static class CommandParser
{
    public static AppAction? Parse(string commandLine)
    {
        string line = commandLine.Trim();
        if (line.Length == 0)
        {
            return null;
        }

        // ":! PROGRAM ARGS" — everything after "!" is the command, so "!ls" and
        // "! ls" both work regardless of the space.
        if (line.StartsWith('!'))
        {
            return new AppAction.RunExternal(line[1..].Trim());
        }

        int space = line.IndexOf(' ');
        string name = space < 0 ? line : line[..space];
        string args = space < 0 ? "" : line[(space + 1)..].Trim();

        return name.ToLowerInvariant() switch
        {
            "cd" => new AppAction.ChangeDirectory(args),
            "pwd" => new AppAction.ShowPath(),
            "trash" => new AppAction.Trash(),
            "delete" => new AppAction.DeletePermanent(),
            "restore-recyclebin" => new AppAction.RestoreFromRecycleBin(),
            "empty-recyclebin" => new AppAction.EmptyRecycleBin(),
            "history" => new AppAction.GoToHistory(args),
            "loadall" => new AppAction.LoadAll(),
            "mkdir" => new AppAction.MakeDir(args),
            "touch" => new AppAction.NewFile(args, MakeParents: false),
            "newfile" => new AppAction.NewFile(args, MakeParents: true),
            "rename" => new AppAction.RenameTo(args),
            "cp" => new AppAction.CopyMove(args, Move: false),
            "mv" => new AppAction.CopyMove(args, Move: true),
            "special" => new AppAction.GoToSpecialFolder(args),
            "clippath" => new AppAction.CopyPaths(args.Length == 0 ? null : args),
            "undo" => new AppAction.Undo(),
            "redo" => new AppAction.Redo(),
            "search" => new AppAction.EnterSearch(args.Length == 0 ? null : args),
            "filter" => new AppAction.EnterFilter(args.Length == 0 ? null : args),
            "set" => new AppAction.SetOption(args),
            "terminal" => new AppAction.OpenTerminal(args),
            "mkshortcut" => new AppAction.MakeShortcut(args),
            "properties" => new AppAction.ShowProperties(args),
            "openwith" => new AppAction.OpenWith(args),
            "zip" => new AppAction.Zip(args),
            "unzip" => new AppAction.Unzip(args),
            "pin" => new AppAction.Pin(args),
            "help" => new AppAction.ShowHelp(args),
            _ => null,
        };
    }
}
