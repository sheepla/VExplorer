namespace VExplorer.App.Features.Help;

/// <summary>One operation and its bindings across the five input routes (原則2).</summary>
public sealed record ActionRow(
    string Action,
    string Vim,
    string Windows,
    string Command,
    string Menu,
    string Mouse
);

/// <summary>A named group of related actions (a section in the help window).</summary>
public sealed record ActionTopic(string Title, IReadOnlyList<ActionRow> Rows);

/// <summary>
/// The operation × input-route table shown by <c>:help</c> — a code projection of
/// <c>docs/specs/VExplorer_ActionMatrix.md</c>, grouped into topics so the help
/// window can present it hierarchically. "—" means "no binding".
/// </summary>
public static class ActionMatrix
{
    private const string None = "—";

    public static IReadOnlyList<ActionTopic> Topics { get; } =
    [
        new ActionTopic(
            "Navigation",
            [
                new("Parent folder", "h / Backspace", "←", None, None, None),
                new("Enter folder", "l", "→", None, "Open", "Double-click"),
                new("Open file (OS)", "Enter", "Enter", None, "Open", "Double-click"),
                new("Cursor up / down", "k / j", "↑ / ↓", None, None, "Click"),
                new("First / last", "g / G", None, None, None, None),
                new("Half page", "Ctrl+D / Ctrl+U", None, None, None, "Scroll"),
                new("Full page", None, "PageDown / PageUp", None, None, "Scroll"),
                new("Change directory", None, None, ":cd [DIR]", None, "Breadcrumb"),
                new("Show path", None, None, ":pwd", None, None),
                new("Special folder", "\\ ~ $", None, ":special NAME", None, None),
                new("Visited history", None, None, ":history [NAME]", None, None),
                new("Address bar", None, "Ctrl+L / F4", None, None, "Address click"),
                new("Reload", "r", "F5", None, None, None),
                new("Load everything", None, None, ":loadall", None, None),
            ]
        ),
        new ActionTopic(
            "Focus & panes",
            [
                new(
                    "Focus Tree / List",
                    "Shift+H / Shift+L",
                    "Shift+← / Shift+→",
                    None,
                    None,
                    "Click"
                ),
                new("Toggle preview", "P", None, None, None, None),
            ]
        ),
        new ActionTopic(
            "Selection",
            [
                new("Range select", "v (VISUAL)", "Shift+↑↓", None, None, "Shift+click / drag"),
                new("Toggle select", "Space", None, None, None, "Ctrl+click"),
                new("Clear selection", "Esc", "Esc", None, None, "Empty click"),
            ]
        ),
        new ActionTopic(
            "File operations",
            [
                new("Copy (yank)", "yy", "Ctrl+C", ":cp SRC... DEST", "Copy", "Ctrl+drag"),
                new("Cut", "dd", "Ctrl+X", ":mv SRC... DEST", "Cut", "Drag"),
                new("Paste", "p", "Ctrl+V", None, "Paste", "Drop"),
                new("Trash", "x", "Delete", ":trash [PATH...]", "Trash", None),
                new(
                    "Delete permanently",
                    None,
                    "Shift+Delete",
                    ":delete [PATH...]",
                    "Delete",
                    None
                ),
                new("Rename", None, "F2", ":rename [NEW]", "Rename", None),
                new("Copy path", "Y", "Shift+Y", ":clippath [PATH]", "Copy Path", None),
                new("New folder", None, None, ":mkdir DIR", "New Folder", None),
                new("New file", None, None, ":touch / :newfile FILE", "New File", None),
                new("Undo", "u", "Ctrl+Z", ":undo", "Undo", None),
                new("Redo", None, "Ctrl+Y / Ctrl+R", ":redo", "Redo", None),
            ]
        ),
        new ActionTopic(
            "Recycle Bin",
            [
                new("Restore (bin)", None, None, ":restore-recyclebin", "(Restore)", None),
                new("Empty Recycle Bin", None, None, ":empty-recyclebin", "(Empty)", None),
            ]
        ),
        new ActionTopic(
            "Shell delegation",
            [
                new("Properties", None, "Alt+Enter", ":properties", "Properties", None),
                new("Open with", None, None, ":openwith [PROG]", "Open with", None),
                new(
                    "Make shortcut",
                    None,
                    None,
                    ":mkshortcut FILE.lnk TARGET",
                    "Create shortcut",
                    None
                ),
                new(
                    "Pin (Programs / Desktop)",
                    None,
                    None,
                    ":pin programs | :pin desktop",
                    "Pin",
                    None
                ),
                new("Zip / unzip", None, None, ":zip [DEST] / :unzip [DEST]", "(Shell ext.)", None),
                new("Open terminal", None, None, ":terminal [DIR]", "(Background)", None),
                new("Run external", "!", None, ":! PROG ARGS", None, None),
            ]
        ),
        new ActionTopic(
            "Search, filter & settings",
            [
                new("Search (jump)", "/", "Ctrl+F", ":search [KW]", None, None),
                new("Next / prev match", "n / N", None, None, None, None),
                new("Filter", "Shift+F", "Shift+F", ":filter [KW]", None, None),
                new("Toggle option", None, None, ":set OPT", None, None),
                new("Help", "?", None, ":help [TOPIC]", None, None),
                new("Sort by column", None, None, None, None, "Column header"),
            ]
        ),
        new ActionTopic(
            "Tabs",
            [
                new("Tab by number", "1–8 / 9 / 0", None, None, None, "Tab click"),
                new("Next / prev tab", "] / [", "Ctrl+Tab / Ctrl+Shift+Tab", None, None, None),
                new("Reorder tab", "} / {", None, None, None, "Tab drag"),
                new("New tab", None, "Ctrl+T", None, None, None),
                new("Close tab", None, "Ctrl+W", None, None, None),
            ]
        ),
    ];

    /// <summary>
    /// Topics filtered to those whose title or any row mentions <paramref name="topic"/>
    /// (all topics when empty). A topic whose title matches keeps all its rows;
    /// otherwise only its matching rows are kept.
    /// </summary>
    public static IReadOnlyList<ActionTopic> Filter(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return Topics;
        }
        List<ActionTopic> result = [];
        foreach (ActionTopic t in Topics)
        {
            if (Contains(t.Title, topic))
            {
                result.Add(t);
                continue;
            }
            List<ActionRow> rows = [.. t.Rows.Where(r => RowMatches(r, topic))];
            if (rows.Count > 0)
            {
                result.Add(t with { Rows = rows });
            }
        }
        return result;
    }

    private static bool RowMatches(ActionRow r, string topic) =>
        Contains(r.Action, topic)
        || Contains(r.Vim, topic)
        || Contains(r.Windows, topic)
        || Contains(r.Command, topic)
        || Contains(r.Menu, topic);

    private static bool Contains(string value, string topic) =>
        value.Contains(topic, StringComparison.OrdinalIgnoreCase);
}
