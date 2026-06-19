using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using R3;
using Serilog;
using Serilog.Formatting.Compact;
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

        ConfigureSerilog();

        WpfProviderInitializer.SetDefaultObservableSystem(ex =>
            Log.Error(ex, "R3 unhandled exception")
        );

        InstallGlobalExceptionHandlers();

        ServiceCollection services = new();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true }
        );

        // Resolve the theme manager early so the initial light/dark palette is
        // applied (following the Windows setting) before any window is shown.
        _serviceProvider.GetRequiredService<Themes.ThemeManager>();

        // Wire the command layer into the dispatcher (the view is attached by the
        // window in its constructor).
        _serviceProvider
            .GetRequiredService<Actions.ActionDispatcher>()
            .AttachCommandHandler(_serviceProvider.GetRequiredService<CommandExecutor>());

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
        catch (Exception ex)
            when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Malformed path → fall through to PC.
            Log.Debug(ex, "Ignoring malformed startup path argument {Arg}", args[0]);
        }
        return (KnownLocations.Pc, null);
    }

    /// <summary>
    /// Configures Serilog with a human-readable rolling text log and a structured
    /// CLEF (compact JSON) log for machine analysis. Logs live under
    /// LocalApplicationData (not the roaming config location) so they stay machine-local.
    /// </summary>
    private static void ConfigureSerilog()
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VExplorer",
            "logs"
        );
        Directory.CreateDirectory(logDir);

        LoggerConfiguration config = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDir, "vexplorer-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
            )
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(logDir, "vexplorer-.clef"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14
            );
#if DEBUG
        config = config.MinimumLevel.Debug();
#else
        config = config.MinimumLevel.Information();
#endif
        Log.Logger = config.CreateLogger();
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
            Log.Error(args.Exception, "Unhandled UI exception");
            TryShowError(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled domain exception");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
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
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        services.AddSingleton<SettingsStore>();
        services.AddSingleton<Core.State.Settings>(sp =>
            sp.GetRequiredService<SettingsStore>().Load()
        );
        services.AddSingleton<Themes.ThemeManager>();
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
        services.AddSingleton<IDragOverlayInterop, WindowsDragOverlayInterop>();
        services.AddSingleton<Features.Menu.ShellMenuHost>();
        services.AddSingleton<IShellInfoProvider, WindowsShellInfoProvider>();
        services.AddSingleton<IIconImageCache, IconImageCache>();
        services.AddSingleton<IOperationHistory, OperationHistory>();
        services.AddSingleton<Diagnostics.ErrorReporter>();
        services.AddSingleton<Features.FileOps.FileOpsService>();

        // Completion engine (stateless / app-wide).
        services.AddSingleton<IPathCompletionSource, WindowsPathCompletionSource>();
        services.AddSingleton<IUncShareSource, WindowsUncShareSource>();
        services.AddSingleton<ISpecialFolderSource, WindowsSpecialFolderSource>();
        services.AddSingleton<ICurrentItemSource, Features.FileOps.CurrentItemSource>();
        services.AddSingleton<ICompletionProvider, PathCompletionProvider>();
        services.AddSingleton<ICompletionProvider, UncPathCompletionProvider>();
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
        services.AddSingleton<Actions.ActionDispatcher>();

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
