using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Application.Abstractions;

public sealed record MonitorProcess(int Pid, string Name, long MemoryMb);

public sealed record BlockedApplication(string Executable, DateTimeOffset BlockedAt, bool HadDebugger);

public interface IMonitorService
{
    Task<IReadOnlyList<MonitorProcess>> ListProcessesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> KillAsync(int pid, string name, CancellationToken cancellationToken = default);
    Task<OperationResult> BlockAsync(string executable, CancellationToken cancellationToken = default);
    Task<OperationResult> UnblockAsync(string executable, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BlockedApplication>> ListBlockedAsync(CancellationToken cancellationToken = default);
}

public interface IHistoryService
{
    IReadOnlyList<HistoryEntry> Entries { get; }
    void Add(HistoryEntry entry);
    void Clear();
    Task<OperationResult> RevertAsync(HistoryEntry entry, IProgress<string>? log = null, CancellationToken cancellationToken = default);
}

/// <summary>Persistence boundary for undo history. Implemented by the infrastructure layer.</summary>
public interface IHistoryStore
{
    IReadOnlyList<HistoryEntry> Load();
    void Save(IReadOnlyList<HistoryEntry> entries);
}

public enum AppTheme
{
    Dark,
    Oled,
    Light
}

public sealed record AppSettings(AppTheme Theme, bool SmoothScrolling);

public interface ISettingsStore
{
    AppSettings Current { get; }
    event EventHandler? Changed;
    void SetTheme(AppTheme theme);
    void SetSmoothScrolling(bool enabled);
    string ActiveProfile { get; }
}

public interface IAuditService
{
    Task<OperationResult<string>> ExportAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);
}

public interface ILicenseService
{
    bool IsPro { get; }
    string TierName { get; }
    bool IsUnlocked(string feature);
    Task<OperationResult> ActivateAsync(string token, CancellationToken cancellationToken = default);
}

public interface IIntegrityService
{
    string Summary { get; }
    Task<OperationResult> VerifyAsync(CancellationToken cancellationToken = default);
}
