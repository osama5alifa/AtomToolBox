namespace Atom.Toolbox.Domain.Maintenance;

public enum StartupKind
{
    Registry,
    Folder
}

public sealed record StartupEntry(
    string Id,
    string Name,
    string Source,
    StartupKind Kind,
    bool IsEnabled,
    string Command);

public sealed record StartupChangeResult(bool Ok, bool Verified, bool Enabled, string? Error = null);

public enum ContextMenuKind
{
    Block,
    Key,
    DWord,
    AddVerb
}

/// <summary>A context-menu definition is static; only the live registry state changes.</summary>
public sealed class ContextMenuDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public ContextMenuKind Kind { get; init; }
    public string? Key { get; init; }

    /// <summary>Key removed when the entry is turned off, when that differs from <see cref="Key"/>.</summary>
    public string? RemoveKey { get; init; }
    public string? ValueName { get; init; }
    public bool WritesMachineState { get; init; }
    public IReadOnlyList<ContextMenuVerbKey> VerbKeys { get; init; } = Array.Empty<ContextMenuVerbKey>();
}

public sealed record ContextMenuVerbKey(
    string Key,
    IReadOnlyDictionary<string, string> Values,
    string Command);

public sealed record StartupScanResult(IReadOnlyList<StartupEntry> Entries, int StaleOverrides);

public sealed record InstalledApp(
    string Name,
    string Publisher,
    string Version,
    long SizeKb,
    string UninstallString,
    bool IsWindowsApp,
    string? PackageFullName,
    string? InstallLocation);

public sealed record DebloatCatalogEntry(
    string Id,
    string Name,
    string Category,
    IReadOnlyList<string> PackageNames,
    string Description);

public sealed record DebloatApp(
    string Id,
    string Name,
    string Category,
    string Description,
    IReadOnlyList<string> PackageNames,
    int InstalledCount,
    int ProvisionedCount,
    int ProtectedCount,
    bool CanRemove)
{
    public bool Installed => InstalledCount > 0;
    public bool Provisioned => ProvisionedCount > 0;
    public bool Absent => InstalledCount == 0 && ProvisionedCount == 0;
}

public sealed record DebloatScanResult(
    IReadOnlyList<DebloatApp> Apps,
    bool IsElevated,
    bool Partial,
    string? ScanError = null);

public sealed record DebloatRemovalResult(
    bool Ok,
    IReadOnlyList<DebloatRemovalOutcome> Results,
    bool Partial = false,
    string? Error = null);

public sealed record DebloatRemovalOutcome(
    string Id,
    string Name,
    bool Ok,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record RestorePoint(int SequenceNumber, string Description, DateTimeOffset CreatedAt);

public sealed record RegistryBackup(string Name, string FilePath, long SizeBytes, DateTimeOffset CreatedAt);

public sealed record CleanerTask(string Id, string Name, string Description);

public sealed record AppCatalogEntry(
    string Id,
    string Name,
    string Category,
    string Url,
    string FileName,
    string InstallType);

public sealed record AppInstallProgress(string AppId, string Stage, int Percent, string? Message = null);

public sealed class ShortcutCategory
{
    public required string Name { get; init; }
    public required IReadOnlyList<ShortcutItem> Items { get; init; }
}

public sealed record ShortcutItem(string Name, string Command);
