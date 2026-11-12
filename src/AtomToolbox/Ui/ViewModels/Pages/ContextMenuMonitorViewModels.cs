using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels.Items;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class ContextMenuViewModel : PageViewModel
{
    private readonly IContextMenuService _menu;
    private readonly IHistoryService _history;
    private readonly IEnvironmentService _environment;
    private readonly Action<string, string?> _toast;
    private readonly Dictionary<string, bool> _states = new(StringComparer.OrdinalIgnoreCase);

    public ContextMenuViewModel(IContextMenuService menu, IHistoryService history, IEnvironmentService environment,
        Action<string, string?> toast)
    {
        _menu = menu;
        _history = history;
        _environment = environment;
        _toast = toast;

        Title = "CONTEXT MENU EDITOR";
        CountText = $"({menu.Entries.Count} ENTRIES)";
        ShowAdminWarning = !environment.IsElevated;
        Intro = "TOGGLE ENTRIES TO ADD OR REMOVE THEM FROM THE RIGHT-CLICK CONTEXT MENU. CHANGES TAKE EFFECT IMMEDIATELY.";
        ShowPreview = true;
        PreviewTitle = "LIVE REGISTRY PREVIEW";
        PreviewText = "-- NO CHANGES --";
    }

    public override async Task LoadAsync()
    {
        IsBusy = true;
        BusyText = "READING CONTEXT-MENU STATE...";
        try
        {
            var state = await _menu.GetStateAsync();
            _states.Clear();
            foreach (var pair in state)
            {
                _states[pair.Key] = pair.Value;
            }

            var rows = new List<object>(_menu.Entries.Count);
            foreach (var definition in _menu.Entries)
            {
                rows.Add(new ToggleRowViewModel(
                    definition.Name,
                    definition.Description,
                    _states.GetValueOrDefault(definition.Id),
                    desired => SetAsync(definition, desired)));
            }

            Sections.Clear();
            Sections.Add(new PageSectionViewModel { Items = rows, Margin = new System.Windows.Thickness(0) });
            UpdatePreview();
        }
        catch (Exception ex)
        {
            _toast("CONTEXT MENU STATE FAILED", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ToggleOutcome> SetAsync(ContextMenuDefinition definition, bool desired)
    {
        OperationResult result;
        try
        {
            result = await _menu.SetAsync(definition, desired);
        }
        catch (UnauthorizedAccessException)
        {
            return ToggleOutcome.Fail(adminRequired: true,
                "Administrator rights are required for this context-menu entry.");
        }
        catch (Exception ex)
        {
            Console.Write($"ERROR: {ex.Message}");
            return ToggleOutcome.Fail(false, ex.Message);
        }

        if (!result.Ok)
        {
            Console.Write($"ERROR: {result.Error}");
            return ToggleOutcome.Fail(
                result.Error?.Contains("denied", StringComparison.OrdinalIgnoreCase) == true,
                result.Error);
        }

        _states[definition.Id] = desired;
        UpdatePreview();
        _history.Add(new HistoryEntry
        {
            Id = definition.Id,
            Name = definition.Name,
            Category = "contextmenu",
            Applied = desired,
            Timestamp = DateTimeOffset.Now
        });
        Console.Write($"{definition.Name}: {(desired ? "APPLIED" : "REVERTED")}");
        _toast("CONTEXT MENU UPDATED", definition.Name);
        return ToggleOutcome.Success;
    }

    private void UpdatePreview()
    {
        var lines = new List<string>();
        foreach (var definition in _menu.Entries)
        {
            if (!_states.GetValueOrDefault(definition.Id))
            {
                continue;
            }

            if (definition.Kind == ContextMenuKind.AddVerb)
            {
                lines.Add($"[HKCU] {string.Join(" + ", definition.VerbKeys.Select(k => k.Key.Split('\\').Last()))}");
            }
            else if (definition.Kind == ContextMenuKind.Block)
            {
                lines.Add($"reg add \"HKLM\\...\\Blocked\" /v \"{definition.ValueName}\" /t REG_SZ /d \"\" /f");
            }
            else if (definition.Kind == ContextMenuKind.Key)
            {
                lines.Add($"reg add \"HKCU\\{definition.Key}\" /f");
            }
            else
            {
                lines.Add($"reg add \"HKCU\\{definition.Key}\" /v \"{definition.ValueName}\" /t REG_DWORD /d 1 /f");
            }
        }

        PreviewText = lines.Count > 0 ? string.Join('\n', lines) : "-- NO CHANGES --";
    }
}

public sealed class MonitorViewModel : PageViewModel
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "secure system", "smss", "csrss",
        "wininit", "winlogon", "services", "lsass", "fontdrvhost", "dwm", "svchost", "audiodg", "wudfhost"
    };

    private readonly IMonitorService _monitor;
    private readonly IEnvironmentService _environment;
    private readonly Action<string, string?> _toast;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private bool _autoRefresh;

    public MonitorViewModel(IMonitorService monitor, IEnvironmentService environment, Action<string, string?> toast)
    {
        _monitor = monitor;
        _environment = environment;
        _toast = toast;
        Title = "BACKGROUND MONITOR";
        ShowAdminWarning = !environment.IsElevated;
        Intro = "TOP PROCESSES BY MEMORY. KILL TERMINATES THE PROCESS; BLOCK PREVENTS FUTURE LAUNCHES (REVERSIBLE VIA UNBLOCK).";
        Toolbar.Add(new ActionButtonViewModel("REFRESH", new AsyncRelayCommand(RefreshAsync)));

        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public override async Task LoadAsync() => await RefreshAsync();

    public override void Deactivate() => _timer.Stop();

    private async Task RefreshAsync()
    {
        IsBusy = true;
        BusyText = "READING PROCESS LIST...";
        try
        {
            var processes = await _monitor.ListProcessesAsync();
            var blocked = await _monitor.ListBlockedAsync();

            Sections.Clear();
            var rows = new List<object>(processes.Count + 2)
            {
                new ToggleRowViewModel(
                    "AUTO REFRESH (5S)",
                    "Only refreshes while this page is visible. Default off for low-end PCs.",
                    _autoRefresh,
                    desired =>
                    {
                        _autoRefresh = desired;
                        if (desired) _timer.Start(); else _timer.Stop();
                        return Task.FromResult(ToggleOutcome.Success);
                    })
            };

            foreach (var process in processes)
            {
                var bare = Path.GetFileNameWithoutExtension(process.Name);
                var isProtected = ProtectedNames.Contains(bare);
                var memory = process.MemoryMb >= 1024
                    ? $"{process.MemoryMb / 1024d:0.00} GB"
                    : $"{process.MemoryMb} MB";
                rows.Add(new ProcessRowViewModel(
                    process.Pid,
                    process.Name,
                    $"PID {process.Pid} | MEM {memory}",
                    isProtected,
                    () => KillAsync(process),
                    () => BlockAsync(process.Name)));
            }

            Sections.Add(new PageSectionViewModel { Items = rows, Margin = new System.Windows.Thickness(0) });
            Sections.Add(new PageSectionViewModel
            {
                Title = "BLOCKED APPLICATIONS",
                IsCard = true,
                Items = blocked.Count == 0
                    ? [new MessageViewModel { Text = "NO BLOCKED APPLICATIONS" }]
                    : blocked.Select(b => (object)new ActionRowViewModel(
                        b.Executable,
                        $"BLOCKED {b.BlockedAt.LocalDateTime:g}" + (b.HadDebugger ? " | PREVIOUS DEBUGGER VALUE SAVED" : ""),
                        "UNBLOCK",
                        () => UnblockAsync(b.Executable))).ToArray()
            });

            CountText = $"{processes.Count} PROCESSES (TOP BY MEMORY)";
        }
        catch (Exception ex)
        {
            _toast("PROCESS SCAN FAILED", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ActionOutcome> KillAsync(MonitorProcess process)
    {
        var result = await _monitor.KillAsync(process.Pid, process.Name);
        Console.Write(result.Ok ? $"{process.Name} terminated." : $"ERROR: {result.Error}");
        _toast(result.Ok ? "PROCESS TERMINATED" : "KILL FAILED", process.Name);
        if (result.Ok)
        {
            _ = RefreshAsync();
        }
        return new ActionOutcome(result.Ok, result.Ok ? "KILLED" : "FAILED");
    }

    private async Task<ActionOutcome> BlockAsync(string name)
    {
        if (!_environment.IsElevated)
        {
            return new ActionOutcome(false, "ADMIN");
        }

        var result = await _monitor.BlockAsync(name);
        Console.Write(result.Ok ? $"{name} blocked (reversible via UNBLOCK)." : $"ERROR: {result.Error}");
        _toast(result.Ok ? "APPLICATION BLOCKED" : "BLOCK FAILED", name);
        if (result.Ok)
        {
            _ = RefreshAsync();
        }
        return new ActionOutcome(result.Ok, result.Ok ? "BLOCKED" : "FAILED");
    }

    private async Task<ActionOutcome> UnblockAsync(string name)
    {
        var result = await _monitor.UnblockAsync(name);
        Console.Write(result.Ok ? $"{name} unblocked." : $"ERROR: {result.Error}");
        if (result.Ok)
        {
            _ = RefreshAsync();
        }
        return new ActionOutcome(result.Ok, result.Ok ? "UNBLOCKED" : "FAILED");
    }
}
