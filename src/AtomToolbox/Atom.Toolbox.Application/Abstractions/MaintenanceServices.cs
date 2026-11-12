using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Application.Abstractions;

public interface IStartupService
{
    Task<StartupScanResult> ScanAsync(CancellationToken cancellationToken = default);
    Task<StartupChangeResult> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken cancellationToken = default);
}

public interface IContextMenuService
{
    IReadOnlyList<ContextMenuDefinition> Entries { get; }
    Task<IReadOnlyDictionary<string, bool>> GetStateAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SetAsync(ContextMenuDefinition definition, bool enabled, CancellationToken cancellationToken = default);
}

public interface IInstalledAppService
{
    Task<IReadOnlyList<InstalledApp>> ListAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> UninstallAsync(InstalledApp app, CancellationToken cancellationToken = default);
}

public interface IDebloatService
{
    Task<DebloatScanResult> ScanAsync(bool force = false, CancellationToken cancellationToken = default);
    Task<DebloatRemovalResult> RemoveAsync(IReadOnlyList<string> ids, IProgress<string>? log = null, CancellationToken cancellationToken = default);
}

public interface ICleanerService
{
    Task<OperationResult> RunAsync(CleanerTask task, IProgress<string>? log = null, CancellationToken cancellationToken = default);
}

public interface IAppInstallerService
{
    Task<OperationResult> InstallAsync(AppCatalogEntry app, IProgress<AppInstallProgress> progress, CancellationToken cancellationToken = default);
    void Cancel(string appId);
}

public interface IBackupService
{
    string BackupDirectory { get; }
    Task<OperationResult> CreateRestorePointAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RestorePoint>> ListRestorePointsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<IReadOnlyList<RegistryBackup>>> CreateRegistryBackupAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RegistryBackup>> ListBackupsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> RestoreAsync(RegistryBackup backup, CancellationToken cancellationToken = default);
}
