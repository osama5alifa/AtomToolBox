using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Application.Tweaks;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels;
using Atom.Toolbox.Infrastructure.Apps;
using Atom.Toolbox.Infrastructure.Catalog;
using Atom.Toolbox.Infrastructure.ContextMenu;
using Atom.Toolbox.Infrastructure.Maintenance;
using Atom.Toolbox.Infrastructure.Power;
using Atom.Toolbox.Infrastructure.Processes;
using Atom.Toolbox.Infrastructure.Registry;
using Atom.Toolbox.Infrastructure.Security;
using Atom.Toolbox.Infrastructure.Startup;
using Atom.Toolbox.Infrastructure.State;
using Atom.Toolbox.Infrastructure.SystemInfo;
using Atom.Toolbox.Infrastructure.Tweaks;

namespace Atom.Toolbox.Desktop;

/// <summary>
/// Composition root. All dependencies are constructed explicitly — no reflection-driven
/// container — so startup cost stays predictable and the object graph is obvious.
/// </summary>
internal sealed class AtomApplication : System.Windows.Application
{
    private TimerResolutionService? _timer;
    private EventWaitHandle? _activator;
    private Thread? _activationThread;

    public ShellViewModel Shell { get; private set; } = null!;

    public void Configure()
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var environment = new EnvironmentService();
        var registry = new RegistryStore();
        var processes = ProcessRunner.Shared;
        var serviceHost = new ServiceHost();

        var catalog = CatalogResources.Tweaks;
        var settings = new SettingsStore(environment);
        var state = new TweakStateStore(environment);
        var license = new LicenseService(environment);
        var integrity = new IntegrityService();

        ThemeManager.Initialize(settings.Current.Theme);
        SmoothScroll.Enable(() => settings.Current.SmoothScrolling);

        var powerPlans = new PowerPlanService(processes, new PowerPlanCatalog(), environment, registry);
        _timer = new TimerResolutionService();
        var accents = new AccentService(registry, environment);
        var executor = new TweakExecutor(registry, serviceHost, processes, environment, powerPlans, accents);

        var startup = new StartupService(registry);
        var history = new HistoryService(new HistoryStore(environment), catalog, executor, state, startup, processes);
        var tweaks = new TweakService(executor, state, history, license);

        var contextMenu = new ContextMenuService(registry);
        var cleaner = new CleanerService(processes, environment, serviceHost);
        var monitor = new MonitorService(registry, environment);
        var debloat = new DebloatService(processes, new DebloatCatalog(), environment);
        var installedApps = new InstalledAppService(processes);
        var installer = new AppInstallerService(environment);
        var backup = new BackupService(processes, environment, registry);
        var audit = new AuditService(catalog, executor, environment);
        var probe = new SystemProbe(environment);

        var pages = new PageFactory(
            catalog,
            tweaks,
            probe,
            environment,
            contextMenu,
            history,
            startup,
            new CleanerCatalog(),
            cleaner,
            monitor,
            new ShortcutCatalog(),
            debloat,
            new AppCatalog(),
            installer,
            installedApps,
            powerPlans,
            new PowerPlanCatalog(),
            backup,
            settings,
            state,
            audit);

        Shell = new ShellViewModel(
            catalog,
            probe,
            settings,
            integrity,
            environment,
            pages,
            theme =>
            {
                ThemeManager.Apply(theme);
                Shell.RefreshThemeDependentState();
                UiTransitions.ThemePulse(MainWindow);
                return Task.CompletedTask;
            });

        var window = new MainWindow(Shell);
        MainWindow = window;
        window.Show();
        StartActivationListener();
    }

    private void StartActivationListener()
    {
        try
        {
            _activator = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AtomToolBox.Activate");
            _activationThread = new Thread(() =>
            {
                while (_activator is not null)
                {
                    try
                    {
                        _activator.WaitOne();
                        Dispatcher.Invoke(() =>
                        {
                            if (MainWindow is not Window window)
                            {
                                return;
                            }

                            if (window.WindowState == WindowState.Minimized)
                            {
                                window.WindowState = WindowState.Normal;
                            }
                            window.Show();
                            window.Activate();
                            window.Topmost = true;
                            window.Topmost = false;
                            window.Focus();
                        });
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("activation", ex);
                    }
                }
            })
            {
                IsBackground = true,
                Name = "AtomToolBox.SingleInstance"
            };
            _activationThread.Start();
        }
        catch (Exception ex)
        {
            AppLog.Error("activation-init", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("dispatcher", e.Exception);
        MessageBox.Show(
            "AtomToolBox hit an unexpected error. Details were written to the log.",
            "Atom ToolBox",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _timer?.Dispose();
        _activator?.Dispose();
        _activator = null;
        base.OnExit(e);
    }
}

internal static class Program
{
    private const string MutexName = @"Local\AtomToolBox.SingleInstance";
    private const string ActivatorName = @"Local\AtomToolBox.Activate";

    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 1;
        }

        if (args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return SelfTest.RunAsync().GetAwaiter().GetResult();
        }

        var skipElevation = Environment.GetEnvironmentVariable("ATOM_NO_ELEVATION") == "1";
        if (!skipElevation && !IsElevated())
        {
            if (!RelaunchElevated())
            {
                MessageBox.Show(
                    "AtomToolBox must run as administrator. The elevated launch was cancelled or blocked by Windows.",
                    "Administrator rights required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            return 0;
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            return 0;
        }

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                {
                    AppLog.Error("domain", exception);
                }
            };

            var application = new AtomApplication();
            application.Configure();
            return application.Run();
        }
        catch (Exception ex)
        {
            AppLog.Error("fatal", ex);
            MessageBox.Show(
                "AtomToolBox could not start. Details were written to %LOCALAPPDATA%\\AtomToolBox\\logs.",
                "Atom ToolBox",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was never owned (secondary instance path).
            }
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool RelaunchElevated()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable) ||
                Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                executable = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
            }

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            Process.Start(startInfo);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivatorName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch (Exception)
        {
            // The existing instance may be shutting down.
        }
    }
}
