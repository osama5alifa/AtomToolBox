using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels.Items;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private readonly ITweakStateStore _state;
    private readonly IAuditService _audit;
    private readonly ITweakCatalog _catalog;
    private readonly IEnvironmentService _environment;
    private readonly Func<AppTheme, Task> _applyTheme;
    private readonly Action<string, string?> _toast;
    private readonly Dictionary<AppTheme, ActionButtonViewModel> _themeButtons = new();

    public SettingsViewModel(
        ISettingsStore settings,
        ITweakStateStore state,
        IAuditService audit,
        ITweakCatalog catalog,
        IEnvironmentService environment,
        Func<AppTheme, Task> applyTheme,
        Action<string, string?> toast)
    {
        _settings = settings;
        _state = state;
        _audit = audit;
        _catalog = catalog;
        _environment = environment;
        _applyTheme = applyTheme;
        _toast = toast;
        Title = "SETTINGS";
        HasConsole = false;
    }

    public override Task LoadAsync()
    {
        Build();
        return Task.CompletedTask;
    }

    private void Build()
    {
        Sections.Clear();
        _themeButtons.Clear();

        foreach (var theme in new[] { AppTheme.Dark, AppTheme.Oled, AppTheme.Light })
        {
            var label = theme.ToString().ToUpperInvariant();
            _themeButtons[theme] = new ActionButtonViewModel(
                label,
                new AsyncRelayCommand(() => SetThemeAsync(theme)),
                _settings.Current.Theme == theme);
        }

        var application = new List<object>
        {
            new ActionRowViewModel(
                "RESET ALL TWEAKS",
                $"Deactivates all toggles across every tweak category ({_state.ActiveCount} currently active)",
                "RESET",
                ResetAsync),
            new SettingsRowViewModel
            {
                Name = "THEME",
                Description = "DEEP GRAY, OLED BLACK OR LIGHT — APPLIED INSTANTLY AND REMEMBERED",
                Actions = _themeButtons.Values.Cast<ActionButtonViewModel>().ToArray()
            },
            new ToggleRowViewModel(
                "SMOOTH SCROLLING",
                "EASED MOUSE-WHEEL SCROLLING WITH A SMALL CURVE — TURN OFF FOR INSTANT STEPS",
                _settings.Current.SmoothScrolling,
                desired =>
                {
                    _settings.SetSmoothScrolling(desired);
                    return Task.FromResult(ToggleOutcome.Success);
                }),
            new SettingsRowViewModel
            {
                Name = "APP VERSION",
                Description = "Atom ToolBox v0.2.5",
                TagText = "0.2.5"
            }
        };

        Sections.Add(new PageSectionViewModel
        {
            Title = "APPLICATION",
            IsCard = true,
            Items = application
        });

        Sections.Add(new PageSectionViewModel
        {
            Title = "LINKS",
            IsCard = true,
            Items =
            [
                Link("YOUTUBE", "youtube.com/@projectatom", "https://www.youtube.com/@projectatom"),
                Link("DISCORD", "discord.gg/9ZCgxhaYV6", "https://discord.gg/9ZCgxhaYV6"),
                Link("X (TWITTER)", "x.com/ProjectAtomOS", "https://x.com/ProjectAtomOS")
            ]
        });

        Sections.Add(new PageSectionViewModel
        {
            Title = "SYSTEM",
            IsCard = true,
            Items =
            [
                new SettingsRowViewModel
                {
                    Name = "ACTIVE TWEAKS",
                    Description = $"{_state.ActiveCount} tweaks currently enabled across all categories",
                    TagText = _state.ActiveCount.ToString()
                },
                new ActionRowViewModel(
                    "REGISTRY AUDIT EXPORT",
                    "SCANS EVERY TWEAK AGAINST THE LIVE SYSTEM AND WRITES A STRUCTURED .TXT REPORT TO YOUR DOWNLOADS FOLDER",
                    "EXPORT AUDIT LOG",
                    ExportAuditAsync),
                new SettingsRowViewModel
                {
                    Name = "INTEGRITY",
                    Description = "CATALOG HASH AND DEBUGGER CHECKS RUN AT STARTUP",
                    TagText = "SYSTEM_SECURE"
                }
            ]
        });
    }

    private SettingsRowViewModel Link(string name, string description, string url) => new()
    {
        Name = name,
        Description = description,
        Actions =
        [
            new ActionButtonViewModel("OPEN", new RelayCommand(() =>
            {
                try
                {
                    _environment.OpenUrl(url);
                }
                catch (Exception ex)
                {
                    _toast("COULD NOT OPEN LINK", ex.Message);
                }
            }))
        ]
    };

    private async Task SetThemeAsync(AppTheme theme)
    {
        _settings.SetTheme(theme);
        await _applyTheme(theme);
        foreach (var pair in _themeButtons)
        {
            pair.Value.IsActive = pair.Key == theme;
        }
        _toast("THEME APPLIED", theme.ToString().ToUpperInvariant());
    }

    private Task<ActionOutcome> ResetAsync()
    {
        _state.Clear();
        _toast("TWEAK STATE RESET", "ALL TOGGLES MARKED INACTIVE");
        Build();
        return Task.FromResult(new ActionOutcome(true, "DONE"));
    }

    private async Task<ActionOutcome> ExportAuditAsync()
    {
        IsBusy = true;
        BusyText = "SCANNING EVERY TWEAK...";
        ShowProgress = true;
        try
        {
            var total = _catalog.TotalTweakCount;
            var progress = new Progress<int>(done =>
            {
                ProgressPercent = total == 0 ? 0 : Math.Min(100, done * 100d / total);
                BusyText = $"EXPORTING {Math.Min(99, (int)ProgressPercent)}%";
            });
            var result = await _audit.ExportAsync(progress);
            if (result.Ok)
            {
                _toast("AUDIT EXPORTED", result.Value);
                return new ActionOutcome(true, "EXPORTED");
            }

            _toast("AUDIT FAILED", result.Error);
            return new ActionOutcome(false, "FAILED");
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
            ShowProgress = false;
        }
    }
}
