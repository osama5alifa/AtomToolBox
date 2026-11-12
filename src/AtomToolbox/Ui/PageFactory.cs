using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels.Items;
using Atom.Toolbox.Desktop.ViewModels.Pages;

namespace Atom.Toolbox.Desktop.ViewModels;

public sealed class PageFactory(
    ITweakCatalog catalog,
    ITweakService tweaks,
    ISystemProbe probe,
    IEnvironmentService environment,
    IContextMenuService contextMenu,
    IHistoryService history,
    IStartupService startup,
    ICleanerCatalog cleanerCatalog,
    ICleanerService cleaner,
    IMonitorService monitor,
    IShortcutCatalog shortcutCatalog,
    IDebloatService debloat,
    IAppCatalog appCatalog,
    IAppInstallerService installer,
    IInstalledAppService installedApps,
    IPowerPlanService powerPlans,
    IPowerPlanCatalog powerPlanCatalog,
    IBackupService backup,
    ISettingsStore settings,
    ITweakStateStore state,
    IAuditService audit)
{
    private ShellViewModel? _shell;

    public void SetShell(ShellViewModel shell) => _shell = shell;

    public PageViewModel? Create(string id)
    {
        Action<string, string?> toast = (message, sub) => _shell?.ShowToast(message, sub);

        return id switch
        {
            "dashboard" => new DashboardViewModel(probe, toast),
            "monitor" => new MonitorViewModel(monitor, environment, toast),
            "shortcuts" => new ShortcutsViewModel(shortcutCatalog, environment, toast),
            "windows" or "gaming" or "gpu" or "power" or "network" or "privacy" or "personalize" or "brave"
                => new TweakPageViewModel(catalog.GetPage(id), tweaks, environment, toast),
            "contextmenu" => new ContextMenuViewModel(contextMenu, history, environment, toast),
            "debloat" => new DebloatViewModel(debloat, environment, toast),
            "cleaner" => new CleanerViewModel(cleanerCatalog, cleaner, toast),
            "powerplans" => new PowerPlansViewModel(powerPlans, powerPlanCatalog, environment, toast),
            "startup" => new StartupViewModel(startup, history, environment, toast),
            "backup" => new BackupViewModel(backup, environment, toast),
            "apps" => new AppsViewModel(appCatalog, installer, toast),
            "applist" => new InstalledAppsViewModel(installedApps, toast),
            "history" => new HistoryViewModel(history, toast),
            "settings" => new SettingsViewModel(settings, state, audit, catalog, environment,
                theme => _shell?.ApplyThemeAsync(theme) ?? Task.CompletedTask, toast),
            "advpower" => Pro("ADVANCED POWER CONFIGURATOR", environment, toast),
            "winupdate" => Pro("WINDOWS UPDATE", environment, toast),
            "dns" => Pro("DNS OPTIMIZER", environment, toast),
            "drivers" => Pro("DRIVER HUB", environment, toast),
            "optimizer" => Pro("ATOM OPTIMIZER", environment, toast),
            _ => null
        };
    }

    private static PageViewModel Pro(string title, IEnvironmentService environment, Action<string, string?> toast) =>
        new ProGateViewModel(title, environment, toast);
}

public sealed class ProGateViewModel : PageViewModel
{
    private const string OptimizerUrl = "https://www.atom-os.com/optimizer";

    public ProGateViewModel(string title, IEnvironmentService environment, Action<string, string?> toast)
    {
        Title = title;
        CountText = "(PRO)";
        Intro = "THIS TOOL ONLY COMES WITH ATOM OPTIMIZER.";
        HasConsole = false;
        ShowAdminWarning = false;

        Toolbar.Add(new ActionButtonViewModel("GET ATOM OPTIMIZER ?", new RelayCommand(() =>
        {
            try
            {
                environment.OpenUrl(OptimizerUrl);
                toast("OPENING ATOM OPTIMIZER", OptimizerUrl);
            }
            catch (Exception ex)
            {
                toast("COULD NOT OPEN BROWSER", ex.Message);
            }
        })));
    }
}