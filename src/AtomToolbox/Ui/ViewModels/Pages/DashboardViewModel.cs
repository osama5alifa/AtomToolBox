using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels.Items;
using Atom.Toolbox.Domain.System;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class DashboardViewModel : PageViewModel
{
    private readonly ISystemProbe _probe;
    private readonly Action<string, string?> _toast;

    public DashboardViewModel(ISystemProbe probe, Action<string, string?> toast)
    {
        _probe = probe;
        _toast = toast;
        Title = "SYSTEM_DASHBOARD";
        HasConsole = false;

        Cpu = new MetricCardViewModel { Label = "CPU_LOAD_INTENSITY", HasBar = true };
        Memory = new MetricCardViewModel { Label = "MEMORY_AVAILABILITY", HasBar = true };
        Disk = new MetricCardViewModel { Label = "SYSTEM_DISK_UTIL", HasBar = true };
        Uptime = new MetricCardViewModel { Label = "ACTIVE_SESSION_TIME" };
        Metrics = [Cpu, Memory, Disk, Uptime];

        InfoCards = new System.Collections.ObjectModel.ObservableCollection<KeyValueViewModel>();
        Toolbar.Add(new ActionButtonViewModel("REFRESH", new AsyncRelayCommand(LoadAsync)));
    }

    public MetricCardViewModel Cpu { get; }
    public MetricCardViewModel Memory { get; }
    public MetricCardViewModel Disk { get; }
    public MetricCardViewModel Uptime { get; }
    public IReadOnlyList<MetricCardViewModel> Metrics { get; }
    public System.Collections.ObjectModel.ObservableCollection<KeyValueViewModel> InfoCards { get; }
    public bool IsElevated { get; private set; }

    public override async Task LoadAsync()
    {
        try
        {
            var snapshot = await _probe.CaptureAsync().ConfigureAwait(true);
            Apply(snapshot);
        }
        catch (Exception ex)
        {
            _toast("SYSTEM INFO UNAVAILABLE", ex.Message);
        }
    }

    private void Apply(SystemSnapshot snapshot)
    {
        Cpu.Value = snapshot.CpuPercent + "%";
        Cpu.Percent = snapshot.CpuPercent;

        Memory.Value = snapshot.MemoryUsedPercent + "%";
        Memory.Percent = snapshot.MemoryUsedPercent;
        Memory.Sub = $"{FormatBytes(snapshot.MemoryFreeBytes)} free / {FormatBytes(snapshot.MemoryTotalBytes)}";

        Disk.Value = snapshot.DiskUsedPercent + "%";
        Disk.Percent = snapshot.DiskUsedPercent;
        Disk.Sub = $"{FormatBytes(snapshot.DiskTotalBytes - snapshot.DiskUsedBytes)} free / {FormatBytes(snapshot.DiskTotalBytes)}";

        Uptime.Value = $"{(int)snapshot.Uptime.TotalHours:00}:{snapshot.Uptime.Minutes:00}:{snapshot.Uptime.Seconds:00}";
        Uptime.Sub = "UPTIME";

        IsElevated = snapshot.IsElevated;
        InfoCards.Clear();
        InfoCards.Add(new KeyValueViewModel { Label = "OPERATING SYSTEM", Value = $"{snapshot.OsName} {snapshot.OsVersion}" });
        InfoCards.Add(new KeyValueViewModel { Label = "BUILD", Value = snapshot.OsBuild });
        InfoCards.Add(new KeyValueViewModel { Label = "HOSTNAME", Value = snapshot.HostName });
        InfoCards.Add(new KeyValueViewModel { Label = "PROCESSOR", Value = snapshot.CpuName });
        InfoCards.Add(new KeyValueViewModel { Label = "LOGICAL CORES", Value = snapshot.LogicalProcessors.ToString() });
        InfoCards.Add(new KeyValueViewModel { Label = "GRAPHICS", Value = snapshot.GpuName });
        InfoCards.Add(new KeyValueViewModel { Label = "ELEVATION", Value = snapshot.IsElevated ? "ADMINISTRATOR" : "STANDARD USER" });
        InfoCards.Add(new KeyValueViewModel { Label = "SESSION", Value = $"{snapshot.Uptime.Days}d {snapshot.Uptime.Hours}h {snapshot.Uptime.Minutes}m" });
        InfoCards.Add(new KeyValueViewModel { Label = "PROFILE", Value = "Windows_NT" });
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var order = (int)Math.Floor(Math.Log(bytes, 1024));
        order = Math.Clamp(order, 0, units.Length - 1);
        return $"{bytes / Math.Pow(1024, order):0.#} {units[order]}";
    }
}
