using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels.Items;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class CleanerViewModel : PageViewModel
{
    private readonly ICleanerService _cleaner;
    private readonly Action<string, string?> _toast;

    public CleanerViewModel(ICleanerCatalog catalog, ICleanerService cleaner, Action<string, string?> toast)
    {
        _cleaner = cleaner;
        _toast = toast;
        Title = "TEMP FILE CLEANER";
        Intro = "CLICK CLEAN TO REMOVE TEMPORARY FILES. NO FILES ARE SCANNED AHEAD — CLEANING IS INSTANT.";

        Sections.Add(new PageSectionViewModel
        {
            Items = catalog.Tasks.Select(task => (object)new ActionRowViewModel(
                task.Name,
                task.Description,
                "CLEAN",
                () => RunAsync(task))).ToArray(),
            Margin = new System.Windows.Thickness(0)
        });
    }

    private async Task<ActionOutcome> RunAsync(CleanerTask task)
    {
        Console.Write($"[{DateTime.Now:HH:mm:ss}] Cleaning: {task.Name}");
        var result = await _cleaner.RunAsync(task, new Progress<string>(Console.Write));
        if (!result.Ok)
        {
            Console.Write($"ERROR: {result.Error}");
            _toast("CLEANUP FAILED", result.Error);
            return new ActionOutcome(false, "FAILED");
        }

        _toast("CLEANUP COMPLETE", task.Name);
        return new ActionOutcome(true, "CLEANED");
    }
}

public sealed class StartupViewModel : PageViewModel
{
    private readonly IStartupService _startup;
    private readonly IHistoryService _history;
    private readonly IEnvironmentService _environment;
    private readonly Action<string, string?> _toast;
    private readonly Dictionary<string, StartupEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private int _activeCount;
    private int _totalCount;

    public StartupViewModel(IStartupService startup, IHistoryService history, IEnvironmentService environment,
        Action<string, string?> toast)
    {
        _startup = startup;
        _history = history;
        _environment = environment;
        _toast = toast;
        Title = "STARTUP MANAGER";
        CountText = "--";
        ShowAdminWarning = !environment.IsElevated;
        Intro = "DISABLE STOPS AN ITEM FROM LAUNCHING AT SIGN-IN WITHOUT DELETING ANYTHING. REGISTRY ENTRIES USE WINDOWS' OWN APPROVED-STATE FLAG (SAME MECHANISM AS TASK MANAGER); STARTUP-FOLDER SHORTCUTS ARE RENAMED WITH A .DISABLED SUFFIX. CHANGES TAKE EFFECT AT NEXT SIGN-IN.";
        Toolbar.Add(new ActionButtonViewModel("REFRESH", new AsyncRelayCommand(LoadAsync)));
    }

    public override async Task LoadAsync()
    {
        IsBusy = true;
        BusyText = "SCANNING STARTUP SOURCES...";
        try
        {
            var scan = await _startup.ScanAsync();
            _entries.Clear();
            _totalCount = scan.Entries.Count;
            _activeCount = scan.Entries.Count(e => e.IsEnabled);

            var rows = new List<object>(scan.Entries.Count);
            foreach (var entry in scan.Entries)
            {
                _entries[entry.Id] = entry;
                rows.Add(new ToggleRowViewModel(
                    entry.Name,
                    $"[{entry.Source}] {entry.Command}",
                    entry.IsEnabled,
                    desired => SetEnabledAsync(entry, desired)));
            }

            Sections.Clear();
            if (rows.Count == 0)
            {
                rows.Add(new MessageViewModel { Text = "NO STARTUP ENTRIES FOUND." });
            }
            Sections.Add(new PageSectionViewModel { Items = rows, Margin = new System.Windows.Thickness(0) });
            CountText = $"({_totalCount} ENTRIES — {_activeCount} ENABLED / {_totalCount - _activeCount} DISABLED)";
            Console.Write($"{_totalCount} startup entries found ({_activeCount} enabled).");
            if (scan.StaleOverrides > 0)
            {
                Console.Write($"NOTE: {scan.StaleOverrides} registry override(s) without a matching Run entry were ignored.");
            }
        }
        catch (Exception ex)
        {
            Sections.Clear();
            Sections.Add(new PageSectionViewModel
            {
                Items = [new MessageViewModel { Text = "SCAN FAILED: " + ex.Message }]
            });
            _toast("STARTUP SCAN FAILED", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    private async Task<ToggleOutcome> SetEnabledAsync(StartupEntry entry, bool desired)
    {
        if (!_environment.IsElevated && entry.Id.StartsWith("run-hklm", StringComparison.OrdinalIgnoreCase))
        {
            return ToggleOutcome.Fail(adminRequired: true, "Administrator rights are required for all-users startup items.");
        }

        var result = await _startup.SetEnabledAsync(entry, desired);
        if (!result.Ok || !result.Verified)
        {
            Console.Write($"ERROR: {result.Error}");
            return ToggleOutcome.Fail(false, result.Error);
        }

        _activeCount += desired ? 1 : -1;
        CountText = $"({_totalCount} ENTRIES — {_activeCount} ENABLED / {_totalCount - _activeCount} DISABLED)";
        _history.Add(new HistoryEntry
        {
            Id = entry.Id,
            Name = entry.Name,
            Category = "startup",
            Applied = !desired,
            Timestamp = DateTimeOffset.Now
        });
        Console.Write($"{entry.Name}: now {(desired ? "ENABLED" : "DISABLED")} (verified).");
        _toast("STARTUP UPDATED", entry.Name);
        return ToggleOutcome.Success;
    }
}
