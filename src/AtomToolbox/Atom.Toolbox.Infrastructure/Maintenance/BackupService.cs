using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Infrastructure.Processes;
using Atom.Toolbox.Infrastructure.Registry;

namespace Atom.Toolbox.Infrastructure.Maintenance;

/// <summary>Restore points through srclient.dll and registry backups through reg.exe export.</summary>
public sealed class BackupService(
    IProcessRunner processes,
    IEnvironmentService environment,
    RegistryStore registry) : IBackupService
{
    private const string SystemRestoreKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    private static readonly (string Key, string Label)[] Branches =
    [
        (@"HKLM\SOFTWARE\Policies", "HKLM_SOFTWARE_Policies"),
        (@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HKLM_Power_Schemes"),
        (@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia", "HKLM_Multimedia"),
        (@"HKCU\Software\Policies", "HKCU_Software_Policies"),
        (@"HKCU\Control Panel", "HKCU_Control_Panel"),
        (@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HKCU_Explorer_Advanced")
    ];

    public string BackupDirectory
    {
        get
        {
            var path = Path.Combine(environment.AppDataDirectory, "backups");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public Task<OperationResult> CreateRestorePointAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateRestorePoint(), cancellationToken);

    private OperationResult CreateRestorePoint()
    {
        if (!environment.IsElevated)
        {
            return OperationResult.Fail("Administrator rights are required to create a restore point.");
        }

        var hadFrequency = registry.TryRead("HKLM", SystemRestoreKey, "SystemRestorePointCreationFrequency", out var previous);
        try
        {
            registry.SetDWord("HKLM", SystemRestoreKey, "SystemRestorePointCreationFrequency", 0);

            var info = new RestorePointInfo
            {
                EventType = 100,             // BEGIN_SYSTEM_CHANGE
                RestorePointType = 12,       // MODIFY_SETTINGS
                Description = "AtomToolBox"
            };

            if (!SRSetRestorePointW(ref info, out var status))
            {
                var error = Marshal.GetLastWin32Error();
                return OperationResult.Fail($"Windows rejected the restore point (0x{error:X8}). System Protection may be disabled.");
            }

            if (status.Status != 0)
            {
                return OperationResult.Fail($"System Restore returned status 0x{status.Status:X8}.");
            }

            return OperationResult.Success;
        }
        finally
        {
            try
            {
                if (hadFrequency)
                {
                    var value = previous.AsText();
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    {
                        registry.SetDWord("HKLM", SystemRestoreKey, "SystemRestorePointCreationFrequency", number);
                    }
                }
                else
                {
                    registry.DeleteValue("HKLM", SystemRestoreKey, "SystemRestorePointCreationFrequency");
                }
            }
            catch (Exception)
            {
                // Restoring the throttle is best effort.
            }
        }
    }

    public async Task<IReadOnlyList<RestorePoint>> ListRestorePointsAsync(CancellationToken cancellationToken = default)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            @(Get-ComputerRestorePoint) | ForEach-Object {
              $time = $_.CreationTime
              [PSCustomObject]@{
                seq = [int]$_.SequenceNumber
                description = [string]$_.Description
                time = $(if ($time -is [datetime]) { $time.ToString('o') } else { [string]$time })
              }
            } | ConvertTo-Json -Compress
            """;

        var result = await processes.RunAsync(PowerShell.Command(script, TimeSpan.FromSeconds(45)), cancellationToken)
            .ConfigureAwait(false);
        using var document = PowerShell.ParseJson(result.StandardOutput);
        if (document is null)
        {
            return [];
        }

        var points = new List<RestorePoint>();
        var root = document.RootElement;
        var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray()
            : root.ValueKind == JsonValueKind.Object ? [root] : [];
        foreach (var item in items)
        {
            var sequence = item.TryGetProperty("seq", out var seq) && seq.TryGetInt32(out var number) ? number : 0;
            var description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
            var timeText = item.TryGetProperty("time", out var time) ? time.GetString() ?? "" : "";
            var created = DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var parsed) ? parsed : DateTimeOffset.MinValue;
            points.Add(new RestorePoint(sequence, description, created));
        }

        return points.OrderByDescending(p => p.SequenceNumber).ToArray();
    }

    public async Task<OperationResult<IReadOnlyList<RegistryBackup>>> CreateRegistryBackupAsync(CancellationToken cancellationToken = default)
    {
        var directory = BackupDirectory;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var files = new List<RegistryBackup>();

        foreach (var (key, label) in Branches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = Path.Combine(directory, $"{label}_{stamp}.reg");
            var result = await processes.RunAsync(new ProcessRequest(
                "reg.exe", ["export", key, file, "/y"], Timeout: TimeSpan.FromSeconds(60)), cancellationToken)
                .ConfigureAwait(false);

            if (result.Ok && File.Exists(file))
            {
                var info = new FileInfo(file);
                files.Add(new RegistryBackup(info.Name, info.FullName, info.Length, info.CreationTime));
            }
        }

        return files.Count > 0
            ? OperationResult<IReadOnlyList<RegistryBackup>>.Success(files)
            : OperationResult<IReadOnlyList<RegistryBackup>>.Fail("No registry branch could be exported.");
    }

    public Task<IReadOnlyList<RegistryBackup>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var directory = BackupDirectory;
        var backups = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.reg")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTime)
                .Select(info => new RegistryBackup(info.Name, info.FullName, info.Length, info.LastWriteTime))
                .ToArray()
            : [];
        return Task.FromResult<IReadOnlyList<RegistryBackup>>(backups);
    }

    public async Task<OperationResult> RestoreAsync(RegistryBackup backup, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(backup.Name);
        if (!name.EndsWith(".reg", StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return OperationResult.Fail("Invalid backup file name.");
        }

        var file = Path.Combine(BackupDirectory, name);
        if (!File.Exists(file))
        {
            return OperationResult.Fail("The backup file no longer exists.");
        }

        var result = await processes.RunAsync(new ProcessRequest(
            "reg.exe", ["import", file], Timeout: TimeSpan.FromMinutes(2)), cancellationToken).ConfigureAwait(false);
        return result.Ok
            ? OperationResult.Success
            : OperationResult.Fail(result.StandardError.Length > 0
                ? result.StandardError.Split('\n')[0].Trim()
                : "The registry backup could not be imported.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestorePointInfo
    {
        public int EventType;
        public int RestorePointType;
        public long SequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateManagerStatus
    {
        public uint Status;
        public long SequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(ref RestorePointInfo info, out StateManagerStatus status);
}
