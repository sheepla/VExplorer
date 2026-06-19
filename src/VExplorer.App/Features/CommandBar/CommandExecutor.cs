using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VExplorer.App.Actions;
using VExplorer.App.Diagnostics;
using VExplorer.App.Features.FileList;
using VExplorer.App.Features.FileOps;
using VExplorer.App.Features.Help;
using VExplorer.App.Settings;
using VExplorer.Core.Actions;
using VExplorer.Core.Commands;
using VExplorer.Core.FileSystem;
using VExplorer.Core.Modes;
using VExplorer.Core.State;

namespace VExplorer.App.Features.CommandBar;

/// <summary>
/// Executes COMMAND-mode command lines. Parsing of the name/arguments lives
/// here; completion metadata lives in the Core command registry. File-operation
/// commands delegate to <see cref="FileOpsService"/> (the same path the keys use).
/// </summary>
public sealed class CommandExecutor(
    ISpecialFolderSource specialFolders,
    FileOpsService fileOps,
    TabManager tabManager,
    ILocationService locationService,
    AppState appState,
    SettingsStore settingsStore,
    IShortcutService shortcuts,
    IShellIntegration shellIntegration,
    ErrorReporter errors,
    ILogger<CommandExecutor> logger
) : ICommandActionHandler
{
    private readonly ISpecialFolderSource _specialFolders = specialFolders;
    private readonly FileOpsService _fileOps = fileOps;
    private readonly TabManager _tabManager = tabManager;
    private readonly ILocationService _locationService = locationService;
    private readonly AppState _appState = appState;
    private readonly SettingsStore _settingsStore = settingsStore;
    private readonly IShortcutService _shortcuts = shortcuts;
    private readonly IShellIntegration _shellIntegration = shellIntegration;
    private readonly ErrorReporter _errors = errors;
    private readonly ILogger<CommandExecutor> _logger = logger;

    private static nint OwnerHwnd =>
        Application.Current?.MainWindow is { } w ? new WindowInteropHelper(w).Handle : 0;

    /// <summary>
    /// Runs a command-specific action routed here by the dispatcher. Returns a
    /// non-null buffer to re-seed the command bar — used by argument-less
    /// <c>:cp</c>/<c>:mv</c> to prompt for a destination; otherwise null. Common
    /// operations (trash, undo, paste, …) are handled by the dispatcher itself.
    /// </summary>
    public string? Handle(AppAction action, ActionContext context)
    {
        TabState tabState = context.Tab;
        FileListViewModel list = context.List;
        switch (action)
        {
            case AppAction.ChangeDirectory cd:
                ExecuteCd(cd.Argument, tabState);
                break;
            case AppAction.ShowPath:
                tabState.SetStatusMessage(
                    tabState.CurrentLocationValue.DisplayName,
                    isError: false
                );
                break;
            case AppAction.GoToSpecialFolder sf:
                ExecuteSpecial(sf.Argument, tabState);
                break;
            case AppAction.GoToHistory h:
                ExecuteHistory(h.Argument, tabState);
                break;
            case AppAction.CopyMove cm:
                return ExecuteCopyMove(cm.Arguments, list, tabState, cm.Move);
            case AppAction.MakeDir md:
                _ = _fileOps.MkdirAsync(tabState, md.Argument, OwnerHwnd);
                break;
            case AppAction.NewFile nf:
                ExecuteNewFile(nf.Argument, tabState, nf.MakeParents);
                break;
            case AppAction.RenameTo rt:
                ExecuteRename(rt.NewName, list, tabState);
                break;
            case AppAction.CopyPaths cp:
                ExecuteClipPath(cp.Arguments ?? "", list, tabState);
                break;
            case AppAction.SetOption so:
                ExecuteSet(so.Arguments, tabState);
                break;
            case AppAction.OpenTerminal t:
                ExecuteTerminal(t.Argument, tabState);
                break;
            case AppAction.MakeShortcut ms:
                ExecuteMkShortcut(ms.Arguments, tabState);
                break;
            case AppAction.ShowProperties p:
                ExecuteProperties(p.Arguments, list, tabState);
                break;
            case AppAction.OpenWith ow:
                ExecuteOpenWith(ow.Arguments, list, tabState);
                break;
            case AppAction.Zip z:
                ExecuteZip(z.Argument, list, tabState);
                break;
            case AppAction.Unzip uz:
                ExecuteUnzip(uz.Argument, list, tabState);
                break;
            case AppAction.Pin pin:
                ExecutePin(pin.Argument, list, tabState);
                break;
            case AppAction.RunExternal ext:
                ExecuteExternal(ext.CommandLine, tabState);
                break;
            case AppAction.ShowHelp h:
                ExecuteHelp(h.Topic);
                break;
            case AppAction.EnterSearch es:
                EnterSearchOrFilter(es.Query ?? "", tabState, filter: false);
                break;
            case AppAction.EnterFilter ef:
                EnterSearchOrFilter(ef.Query ?? "", tabState, filter: true);
                break;
            default:
                tabState.SetStatusMessage($"Unhandled command action: {action.GetType().Name}");
                break;
        }
        return null;
    }

    private void ExecuteRename(string newName, FileListViewModel list, TabState tabState)
    {
        FileItemRow? cursor = list.CursorRow;
        if (cursor is null || cursor.IsParentEntry || newName.Length == 0)
        {
            tabState.SetStatusMessage("rename: usage :rename NEWNAME");
            return;
        }
        _ = _fileOps.RenameAsync(cursor.FullPath, newName, tabState, OwnerHwnd);
    }

    /// <summary>
    /// <c>:cp</c> / <c>:mv</c>. Argument semantics:
    /// one arg → it is the destination, sources = focus targets (selection/cursor);
    /// two+ args → the last is the destination, the rest are sources.
    /// (Paths are split on spaces; quoting is not yet supported.)
    /// </summary>
    private string? ExecuteCopyMove(
        string args,
        FileListViewModel list,
        TabState tabState,
        bool move
    )
    {
        string verb = move ? "mv" : "cp";
        string cwd = tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory();

        string[] tokens = args.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (tokens.Length == 0)
        {
            // No destination yet → prompt for it by re-seeding the command bar.
            tabState.SetStatusMessage($"{(move ? "Move" : "Copy")} to:", isError: false);
            return $"{verb} ";
        }

        string dest = Resolve(tokens[^1], cwd);

        IReadOnlyList<string> sources =
            tokens.Length == 1
                ? list.ResolveTargetPaths()
                : tokens[..^1].Select(t => Resolve(t, cwd)).ToList();

        if (sources.Count == 0)
        {
            tabState.SetStatusMessage("No items to operate on");
            return null;
        }

        // Unix-style: existing directory → into it; otherwise treat DEST as the
        // target name/path (single source only). Overwrite prompts stay with the OS.
        if (Directory.Exists(dest))
        {
            _ = _fileOps.DropAsync(sources, dest, copy: !move, OwnerHwnd, tabState);
        }
        else if (sources.Count == 1)
        {
            if (move)
            {
                _ = _fileOps.MoveToPathAsync(sources[0], dest, tabState, OwnerHwnd);
            }
            else
            {
                _ = _fileOps.CopyToPathAsync(sources[0], dest, tabState, OwnerHwnd);
            }
        }
        else
        {
            tabState.SetStatusMessage($"Target must be a directory: {dest}");
        }
        return null;
    }

    private void ExecuteHistory(string arg, TabState tabState)
    {
        if (arg.Length == 0)
        {
            tabState.SetStatusMessage(
                "history: usage :history PATH (Tab to pick a visited folder)"
            );
            return;
        }
        if (_locationService.TryResolve(arg, out Location location))
        {
            tabState.NavigateTo(location);
        }
        else
        {
            tabState.SetStatusMessage($"Not found: {arg}");
        }
    }

    /// <summary>
    /// <c>:special NAME</c> — jump to a Windows known folder by name. Resolves the
    /// name directly, then as a <c>shell:</c> parsing name, then as an existing path
    /// (Tab completion inserts the physical path).
    /// </summary>
    private void ExecuteSpecial(string arg, TabState tabState)
    {
        if (arg.Length == 0)
        {
            tabState.SetStatusMessage("special: usage :special NAME (Tab to pick a known folder)");
            return;
        }
        if (
            _locationService.TryResolve(arg, out Location direct)
            || _locationService.TryResolve($"shell:{arg}", out direct)
        )
        {
            tabState.NavigateTo(direct);
        }
        else
        {
            tabState.SetStatusMessage($"Unknown special folder: {arg}");
        }
    }

    /// <summary>
    /// <c>:clippath [PATH...]</c> — copy paths as text. With no argument this is the
    /// <c>Y</c> key (selection/cursor); with arguments it copies the given paths.
    /// </summary>
    private void ExecuteClipPath(string args, FileListViewModel list, TabState tabState)
    {
        if (args.Length == 0)
        {
            _fileOps.CopyPathsAsText(list, tabState);
            return;
        }
        string cwd = tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory();
        string[] tokens = args.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        string text = string.Join(Environment.NewLine, tokens.Select(t => Resolve(t, cwd)));
        ShellClipboard.SetText(text);
        tabState.SetStatusMessage($"Copied {tokens.Length} path(s)", isError: false);
    }

    /// <summary>
    /// <c>:search</c> / <c>:filter</c>. With a keyword, runs immediately and (search)
    /// keeps matches for n/N; without one, enters the incremental SEARCH/FILTER bar
    /// (same as <c>/</c> or <c>Shift+F</c>). Deferred so the mode change applies after
    /// the command bar has closed back to NORMAL (entering a submode from COMMAND is
    /// an illegal transition).
    /// </summary>
    private static void EnterSearchOrFilter(string keyword, TabState tabState, bool filter)
    {
        string query = keyword.Trim();
        Application.Current?.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                tabState.DispatchModeEvent(
                    filter ? new ModeEvent.EnterFilter() : new ModeEvent.EnterSearch()
                );
                if (query.Length > 0)
                {
                    tabState.DispatchModeEvent(new ModeEvent.UpdateQuery(query));
                    tabState.DispatchModeEvent(new ModeEvent.ConfirmMode());
                    tabState.DispatchModeEvent(new ModeEvent.ExitToNormal());
                }
                // No keyword: stay in the submode; the query bar auto-focuses when shown.
            })
        );
    }

    /// <summary>
    /// <c>:touch</c> / <c>:newfile</c> — create an empty file. <paramref name="makeParents"/>
    /// distinguishes the two: <c>:newfile</c> creates intermediate directories.
    /// </summary>
    private void ExecuteNewFile(string arg, TabState tabState, bool makeParents)
    {
        if (arg.Length == 0)
        {
            tabState.SetStatusMessage(
                $"{(makeParents ? "newfile" : "touch")}: usage provide a FILE path"
            );
            return;
        }
        string cwd = tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory();
        string full = Resolve(arg, cwd);
        if (makeParents)
        {
            _ = _fileOps.NewFileAsync(tabState, full, OwnerHwnd);
        }
        else
        {
            _ = _fileOps.TouchAsync(tabState, full, OwnerHwnd);
        }
    }

    /// <summary>
    /// <c>:set</c> — toggle/set a display or behaviour option. Applies immediately
    /// (refreshes open tabs) and persists to config so it survives a restart.
    /// </summary>
    private void ExecuteSet(string args, TabState tabState)
    {
        SetResult r = SettingsCommand.Apply(_appState.Settings, args);
        if (r.IsError)
        {
            tabState.SetStatusMessage(r.Message);
            return;
        }
        if (r.Updated is Core.State.Settings updated)
        {
            _appState.UpdateSettings(updated);
            _settingsStore.Save(updated);
            _tabManager.RefreshAllTabs();
        }
        if (r.Message.Length > 0)
        {
            tabState.SetStatusMessage(r.Message, isError: false);
        }
    }

    /// <summary><c>:!</c> / <c>!</c> — launch an external program (with args) from the current folder.</summary>
    private void ExecuteExternal(string commandLine, TabState tabState)
    {
        if (commandLine.Length == 0)
        {
            tabState.SetStatusMessage("usage: :! PROGRAM [ARGS]");
            return;
        }
        string cwd = tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory();
        int sp = commandLine.IndexOf(' ');
        string file = sp < 0 ? commandLine : commandLine[..sp];
        string arguments = sp < 0 ? "" : commandLine[(sp + 1)..];
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = arguments,
                    UseShellExecute = true,
                    WorkingDirectory = cwd,
                }
            );
            tabState.SetStatusMessage($"Launched {file}", isError: false);
        }
        catch (Exception ex)
        {
            _errors.Report(tabState, "Launch external program", ex, ("File", file), ("Cwd", cwd));
        }
    }

    /// <summary><c>:terminal [DIR]</c> — open a terminal at DIR (or the current folder).</summary>
    private void ExecuteTerminal(string args, TabState tabState)
    {
        string baseDir = tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory();
        string dir = args.Length > 0 ? Resolve(args, baseDir) : baseDir;
        if (!Directory.Exists(dir))
        {
            tabState.SetStatusMessage($"Directory not found: {dir}");
            return;
        }
        // Prefer Windows Terminal, then PowerShell, then the classic console.
        foreach (string exe in (string[])["wt.exe", "pwsh.exe", "powershell.exe", "cmd.exe"])
        {
            try
            {
                ProcessStartInfo psi =
                    exe == "wt.exe"
                        ? new ProcessStartInfo(exe, $"-d \"{dir}\"") { UseShellExecute = true }
                        : new ProcessStartInfo(exe)
                        {
                            WorkingDirectory = dir,
                            UseShellExecute = true,
                        };
                Process.Start(psi);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Terminal {Exe} unavailable, trying next", exe);
            }
        }
        tabState.SetStatusMessage("Could not open a terminal");
    }

    /// <summary><c>:mkshortcut FILE.lnk TARGET</c> — create a .lnk shortcut (delegated to the shell).</summary>
    private void ExecuteMkShortcut(string args, TabState tabState)
    {
        string cwd = tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory();
        string[] tokens = args.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (tokens.Length < 2)
        {
            tabState.SetStatusMessage("usage: :mkshortcut FILE.lnk TARGET");
            return;
        }
        string link = Resolve(tokens[0], cwd);
        if (!link.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            link += ".lnk";
        }
        string target = Resolve(tokens[1], cwd);
        string? err = _shortcuts.Create(link, target);
        if (err == null)
        {
            tabState.RequestRefresh(Path.GetFileName(link));
            tabState.SetStatusMessage($"Created shortcut \"{Path.GetFileName(link)}\"", false);
        }
        else
        {
            tabState.SetStatusMessage(err);
        }
    }

    /// <summary><c>:properties [PATH]</c> — open the shell Properties dialog.</summary>
    private void ExecuteProperties(string args, FileListViewModel list, TabState tabState)
    {
        IReadOnlyList<string> targets = ResolveTargets(args, list, tabState);
        if (targets.Count == 0)
        {
            tabState.SetStatusMessage("No items to operate on");
            return;
        }
        _shellIntegration.ShowProperties(targets[0], OwnerHwnd);
        if (targets.Count > 1)
        {
            tabState.SetStatusMessage(
                "Showing properties of the first item (multi-select is a future item)",
                isError: false
            );
        }
    }

    /// <summary><c>:openwith [PROG]</c> — launch PROG with the target, or show the OS picker.</summary>
    private void ExecuteOpenWith(string args, FileListViewModel list, TabState tabState)
    {
        IReadOnlyList<string> targets = list.ResolveTargetPaths();
        if (targets.Count == 0)
        {
            tabState.SetStatusMessage("No items to operate on");
            return;
        }
        string target = targets[0];
        if (args.Length == 0)
        {
            _shellIntegration.ShowOpenWith(target, OwnerHwnd);
            return;
        }
        int sp = args.IndexOf(' ');
        string prog = sp < 0 ? args : args[..sp];
        string extra = sp < 0 ? "" : args[(sp + 1)..];
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = prog,
                    Arguments = $"{extra} \"{target}\"".Trim(),
                    UseShellExecute = true,
                }
            );
        }
        catch (Exception ex)
        {
            tabState.SetStatusMessage(ex.Message);
        }
    }

    /// <summary><c>:zip [DEST]</c> — bundle selection/cursor into a single archive.</summary>
    private void ExecuteZip(string args, FileListViewModel list, TabState tabState)
    {
        IReadOnlyList<string> targets = list.ResolveTargetPaths();
        if (targets.Count == 0)
        {
            tabState.SetStatusMessage("No items to operate on");
            return;
        }
        string cwd = tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory();
        string dest;
        if (args.Length > 0)
        {
            string a = Resolve(args, cwd);
            dest =
                Directory.Exists(a) ? Path.Combine(a, DefaultZipName(targets, cwd))
                : a.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? a
                : a + ".zip";
        }
        else
        {
            dest = Path.Combine(cwd, DefaultZipName(targets, cwd));
        }
        dest = UniqueFile(dest);
        string? err = ZipService.Create(targets, dest);
        if (err == null)
        {
            tabState.RequestRefresh(Path.GetFileName(dest));
            tabState.SetStatusMessage($"Created \"{Path.GetFileName(dest)}\"", false);
        }
        else
        {
            tabState.SetStatusMessage(err);
        }
    }

    /// <summary><c>:unzip [DEST]</c> — extract the cursor's zip into a folder under DEST/cwd.</summary>
    private void ExecuteUnzip(string args, FileListViewModel list, TabState tabState)
    {
        IReadOnlyList<string> targets = list.ResolveTargetPaths();
        if (targets.Count == 0)
        {
            tabState.SetStatusMessage("No items to operate on");
            return;
        }
        string zip = targets[0];
        if (!zip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(zip))
        {
            tabState.SetStatusMessage("unzip: cursor is not a .zip file");
            return;
        }
        string cwd = tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory();
        string destBase = args.Length > 0 ? Resolve(args, cwd) : cwd;
        string? err = ZipService.Extract(zip, destBase, out string createdFolder);
        if (err == null)
        {
            // Focus the created folder when it landed in the current directory.
            bool inCwd = string.Equals(
                destBase.TrimEnd(Path.DirectorySeparatorChar),
                cwd.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase
            );
            tabState.RequestRefresh(inCwd ? createdFolder : null);
            tabState.SetStatusMessage($"Extracted \"{Path.GetFileName(zip)}\"", false);
        }
        else
        {
            tabState.SetStatusMessage(err);
        }
    }

    /// <summary>
    /// <c>:pin programs</c> / <c>:pin desktop</c> — drop a shortcut to the target in
    /// the Start Menu programs folder (the app list) or on the Desktop. The verb is
    /// named "programs" because that is literally where the shortcut lands; pinning
    /// to the Start tiles has no supported API.
    /// </summary>
    private void ExecutePin(string args, FileListViewModel list, TabState tabState)
    {
        string sub = args.Trim().ToLowerInvariant();
        string folder = sub switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "programs" => Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            _ => "",
        };
        if (folder.Length == 0)
        {
            tabState.SetStatusMessage("usage: :pin programs | :pin desktop");
            return;
        }
        IReadOnlyList<string> targets = list.ResolveTargetPaths();
        if (targets.Count == 0)
        {
            tabState.SetStatusMessage("No items to operate on");
            return;
        }
        string target = targets[0];
        string link = Path.Combine(
            folder,
            Path.GetFileNameWithoutExtension(target.TrimEnd(Path.DirectorySeparatorChar)) + ".lnk"
        );
        string? err = _shortcuts.Create(link, target);
        if (err == null)
        {
            tabState.SetStatusMessage(
                sub == "programs"
                    ? "Added to the Start menu app list"
                    : "Created a desktop shortcut",
                isError: false
            );
        }
        else
        {
            tabState.SetStatusMessage(err);
        }
    }

    /// <summary>Targets from explicit args (split + resolved) else the selection/cursor.</summary>
    private IReadOnlyList<string> ResolveTargets(
        string args,
        FileListViewModel list,
        TabState tabState
    )
    {
        if (args.Length == 0)
        {
            return list.ResolveTargetPaths();
        }
        string cwd = tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory();
        return
        [
            .. args.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Select(t => Resolve(t, cwd)),
        ];
    }

    private static string DefaultZipName(IReadOnlyList<string> targets, string cwd)
    {
        if (targets.Count == 1)
        {
            return Path.GetFileNameWithoutExtension(targets[0].TrimEnd(Path.DirectorySeparatorChar))
                + ".zip";
        }
        string folder = Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar));
        return (folder.Length > 0 ? folder : "Archive") + ".zip";
    }

    /// <summary>Returns <paramref name="path"/> or "name (2).ext" if it already exists.</summary>
    private static string UniqueFile(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }
        string dir = Path.GetDirectoryName(path) ?? "";
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary><c>:help [TOPIC]</c> — open the action × input-route reference popup.</summary>
    private static void ExecuteHelp(string topic)
    {
        HelpWindow window = new(topic) { Owner = Application.Current?.MainWindow };
        window.Show();
    }

    private void ExecuteCd(string arg, TabState tabState)
    {
        // No argument → home (shell convention).
        if (arg.Length == 0)
        {
            tabState.NavigateTo(_specialFolders.GetHomeDirectory());
            return;
        }

        // A known location name / shell parsing name (PC, shell:Documents, ::{CLSID}).
        if (_locationService.TryResolve(arg, out Location direct))
        {
            tabState.NavigateTo(direct);
            return;
        }

        // Otherwise treat as a (possibly relative) filesystem path.
        string target = Resolve(
            arg,
            tabState.CurrentDirectoryPath ?? _specialFolders.GetHomeDirectory()
        );
        if (Directory.Exists(target))
        {
            tabState.NavigateTo(target);
        }
        else
        {
            tabState.SetStatusMessage($"Directory not found: {target}");
        }
    }

    private string Resolve(string path, string currentDirectory)
    {
        return PathResolver.Resolve(path, currentDirectory, _specialFolders.GetHomeDirectory());
    }
}
