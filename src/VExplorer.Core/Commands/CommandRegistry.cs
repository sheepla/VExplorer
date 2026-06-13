using System.Collections.Immutable;
using VExplorer.Core.Completion;

namespace VExplorer.Core.Commands;

/// <summary>
/// The set of known commands. Source of names for CommandName completion and of
/// argument-completion metadata for the context resolver. Only commands that
/// are actually executable are registered, so completion never offers a no-op.
/// </summary>
public sealed class CommandRegistry
{
    private readonly ImmutableArray<CommandSpec> _specs;
    private readonly IReadOnlyDictionary<string, CommandSpec> _byName;

    public CommandRegistry(IEnumerable<CommandSpec> specs)
    {
        _specs = [.. specs];
        _byName = _specs.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CommandSpec> Commands => _specs;

    public CommandSpec? Find(string name)
    {
        return _byName.TryGetValue(name, out CommandSpec? spec) ? spec : null;
    }

    /// <summary>The default registry for the currently implemented commands.</summary>
    public static CommandRegistry Default { get; } =
        new([
            // :cd [DIR] — navigate; the directory argument completes as a path.
            new CommandSpec("cd", [CompletionContextKind.Path], LastArgumentRepeats: true),
            // :pwd — show the current path; takes no arguments.
            new CommandSpec("pwd", []),
            // File operations (implicit target = selection else cursor).
            new CommandSpec("trash", [CompletionContextKind.Path], LastArgumentRepeats: true),
            new CommandSpec("delete", [CompletionContextKind.Path], LastArgumentRepeats: true),
            new CommandSpec("cp", [CompletionContextKind.Path], LastArgumentRepeats: true),
            new CommandSpec("mv", [CompletionContextKind.Path], LastArgumentRepeats: true),
            new CommandSpec("rename", [CompletionContextKind.CurrentName]),
            new CommandSpec("mkdir", []),
            // :touch / :newfile FILE — create an empty file; arg completes as a path.
            new CommandSpec("touch", [CompletionContextKind.Path]),
            new CommandSpec("newfile", [CompletionContextKind.Path]),
            // Recycle Bin operations (act on selection/cursor when inside the bin).
            // Named "-recyclebin" to pair with :empty-recyclebin in completion.
            new CommandSpec("restore-recyclebin", []),
            new CommandSpec("empty-recyclebin", []),
            // :history PATH — jump to a visited location; arg completes from history.
            new CommandSpec("history", [CompletionContextKind.NavigationHistory]),
            // :loadall — re-list the current folder with no time budget (completes a
            // listing that the time budget truncated).
            new CommandSpec("loadall", []),
            // :special NAME — jump to a Windows known folder by name.
            new CommandSpec("special", [CompletionContextKind.SpecialFolder]),
            // :clippath [PATH] — copy paths as text (Y key equivalent).
            new CommandSpec("clippath", [CompletionContextKind.Path], LastArgumentRepeats: true),
            // Undo / redo — the same operation history as u / Ctrl+Z / Ctrl+Y.
            new CommandSpec("undo", []),
            new CommandSpec("redo", []),
            // :search / :filter [KEYWORD] — run immediately with a keyword, else enter
            // the incremental SEARCH / FILTER bar. Keywords take no completion.
            new CommandSpec("search", []),
            new CommandSpec("filter", []),
            // :set OPTION — toggle/set a display option; options complete by name.
            new CommandSpec("set", [CompletionContextKind.SetOption], LastArgumentRepeats: true),
            // External / shell-delegation commands.
            new CommandSpec("terminal", [CompletionContextKind.Path]),
            new CommandSpec(
                "!",
                [CompletionContextKind.ExternalCommand],
                LastArgumentRepeats: true
            ),
            new CommandSpec("mkshortcut", [CompletionContextKind.Path], LastArgumentRepeats: true),
            new CommandSpec("properties", [CompletionContextKind.Path], LastArgumentRepeats: true),
            new CommandSpec("openwith", []),
            new CommandSpec("zip", [CompletionContextKind.Path]),
            new CommandSpec("unzip", [CompletionContextKind.Path]),
            // :pin programs|desktop — drop a .lnk in the Start app list or on the Desktop.
            new CommandSpec("pin", []),
            // :help [TOPIC] — open the key/command reference popup.
            new CommandSpec("help", []),
        ]);
}
