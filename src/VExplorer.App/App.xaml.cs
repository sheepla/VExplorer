using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using R3;
using VExplorer.App.Features.AddressBar;
using VExplorer.App.Features.CommandBar;
using VExplorer.App.Features.FileList;
using VExplorer.App.Features.Shell;
using VExplorer.App.Features.StatusBar;
using VExplorer.App.Features.Tree;
using VExplorer.App.Settings;
using VExplorer.Core.Commands;
using VExplorer.Core.Completion;
using VExplorer.Core.FileSystem;
using VExplorer.Core.State;
using VExplorer.Shell.FileSystem;

namespace VExplorer.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private TabManager? _tabManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        WpfProviderInitializer.SetDefaultObservableSystem(ex =>
            Trace.WriteLine($"R3 UnhandledException: {ex}")
        );

        InstallGlobalExceptionHandlers();

        ServiceCollection services = new();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true }
        );

        _tabManager = _serviceProvider.GetRequiredService<TabManager>();
        (Location initial, string? focusName) = ResolveStartupLocation(e.Args);
        _tabManager.OpenTab(initial);
        if (focusName != null)
        {
            // Consumed by the file list on its initial load (no refresh needed).
            _tabManager.GetActiveTabState().SetPendingFocusName(focusName);
        }

        MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <summary>
    /// Picks the startup location from a command-line path argument: a directory
    /// opens directly; a file opens its parent with the file focused; anything
    /// else falls back to the PC root.
    /// </summary>
    private static (Location Location, string? FocusName) ResolveStartupLocation(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return (KnownLocations.Pc, null);
        }
        try
        {
            string full = Path.GetFullPath(args[0]);
            if (Directory.Exists(full))
            {
                return (Location.ForPath(full), null);
            }
            if (File.Exists(full) && Path.GetDirectoryName(full) is string parent)
            {
                return (Location.ForPath(parent), Path.GetFileName(full));
            }
        }
        catch
        {
            // Malformed path → fall through to PC.
        }
        return (KnownLocations.Pc, null);
    }

    /// <summary>
    /// Last-resort safety net: surface non-fatal exceptions in the status bar
    /// instead of crashing. Truly fatal failures (OOM, stack overflow) cannot be
    /// recovered and are only logged.
    /// </summary>
    private void InstallGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Trace.WriteLine($"Unhandled UI exception: {args.Exception}");
            TryShowError(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Trace.WriteLine($"Unhandled domain exception: {args.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Trace.WriteLine($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };
    }

    private void TryShowError(Exception ex)
    {
        try
        {
            _tabManager
                ?.GetActiveTabState()
                .SetStatusMessage($"Error: {ex.Message}", isError: true);
        }
        catch
        {
            // Never throw from the error handler.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tabManager?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<Core.State.Settings>(sp =>
            sp.GetRequiredService<SettingsStore>().Load()
        );
        services.AddSingleton<AppState>(sp => new AppState(
            sp.GetRequiredService<Core.State.Settings>()
        ));
        services.AddSingleton<TabManager>(sp => new TabManager(
            sp,
            sp.GetRequiredService<AppState>()
        ));
        // Physical lister wrapped by the virtual lister, which intercepts the PC
        // root and virtual destinations and delegates physical paths to it.
        services.AddSingleton<WindowsDirectoryLister>();
        services.AddSingleton<ILocationService, WindowsLocationService>();
        services.AddSingleton<IRecycleBinSource, RecycleBinShell>();
        services.AddSingleton<INetworkSource, WindowsNetworkSource>();
        services.AddSingleton<IDirectoryLister>(sp => new VirtualDirectoryLister(
            sp.GetRequiredService<WindowsDirectoryLister>(),
            sp.GetRequiredService<ILocationService>(),
            sp.GetRequiredService<IRecycleBinSource>(),
            sp.GetRequiredService<INetworkSource>()
        ));
        services.AddSingleton<IShellFileOps, WindowsShellFileOps>();
        services.AddSingleton<IShortcutService, WindowsShortcutService>();
        services.AddSingleton<IShellIntegration, WindowsShellIntegration>();
        services.AddSingleton<IShellContextMenu, WindowsShellContextMenu>();
        services.AddSingleton<Features.Menu.ShellMenuHost>();
        services.AddSingleton<IShellInfoProvider, WindowsShellInfoProvider>();
        services.AddSingleton<IIconImageCache, IconImageCache>();
        services.AddSingleton<IOperationHistory, OperationHistory>();
        services.AddSingleton<Features.FileOps.FileOpsService>();

        // Completion engine (stateless / app-wide).
        services.AddSingleton<IPathCompletionSource, WindowsPathCompletionSource>();
        services.AddSingleton<ISpecialFolderSource, WindowsSpecialFolderSource>();
        services.AddSingleton<ICurrentItemSource, Features.FileOps.CurrentItemSource>();
        services.AddSingleton<ICompletionProvider, PathCompletionProvider>();
        services.AddSingleton<ICompletionProvider, SpecialFolderCompletionProvider>();
        services.AddSingleton<ICompletionProvider, CommandNameCompletionProvider>();
        services.AddSingleton<ICompletionProvider, CurrentNameCompletionProvider>();
        services.AddSingleton<ICompletionProvider, SetOptionCompletionProvider>();
        services.AddSingleton<
            ICompletionProvider,
            Features.Completion.NavigationHistoryCompletionProvider
        >();
        services.AddSingleton<CompletionEngine>();

        // Command system (names + completion metadata in Core, execution in App).
        services.AddSingleton(CommandRegistry.Default);
        services.AddSingleton<ICommandHistory, CommandHistory>();
        services.AddSingleton<CommandContextResolver>();
        services.AddSingleton<CommandExecutor>();

        services.AddSingleton<Features.Tabs.TabBarViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        services.AddScoped<TabState>();
        services.AddScoped<FileListViewModel>();
        services.AddScoped<TreeViewModel>();
        services.AddScoped<StatusBarViewModel>();
        services.AddScoped<AddressBarViewModel>();
        services.AddScoped<CommandBarViewModel>();
        services.AddScoped<Features.Search.SearchFilterBarViewModel>();
    }
}
