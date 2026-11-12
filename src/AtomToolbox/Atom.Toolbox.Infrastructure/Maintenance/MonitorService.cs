using System.Diagnostics;
using System.Text.Json;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Infrastructure.Registry;
using Atom.Toolbox.Infrastructure.State;

namespace Atom.Toolbox.Infrastructure.Maintenance;

/// <summary>
/// Process monitor and reversible application blocking through Image File Execution Options.
/// The previous Debugger value is preserved so UNBLOCK always restores the machine exactly.
/// </summary>
public sealed class MonitorService(RegistryStore registry, IEnvironmentService environment) : IMonitorService
{
    private const string IfeoRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Image File Execution Options";

    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "secure system", "smss", "csrss",
        "wininit", "winlogon", "services", "lsass", "fontdrvhost", "dwm", "svchost", "audiodg", "wudfhost"
    };

    private readonly string _storePath = Path.Combine(environment.AppDataDirectory, "blocked.json");
    private readonly object _gate = new();

    public Task<IReadOnlyList<MonitorProcess>> ListProcessesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<MonitorProcess>>(() =>
        {
            var list = new List<MonitorProcess>(256);
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        list.Add(new MonitorProcess(process.Id, process.ProcessName + ".exe", process.WorkingSet64 / 1024 / 1024));
                    }
                    catch (Exception)
                    {
                        // Exited while enumerating.
                    }
                }
            }

            return list.OrderByDescending(p => p.MemoryMb).Take(40).ToArray();
        }, cancellationToken);

    public Task<OperationResult> KillAsync(int pid, string name, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (pid <= 4)
            {
                return OperationResult.Fail("Invalid process id.");
            }

            var bare = Path.GetFileNameWithoutExtension(name);
            if (Protected.Contains(bare))
            {
                return OperationResult.Fail($"\"{bare}\" is a protected system process.");
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                if (!string.Equals(process.ProcessName, bare, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult.Fail("The process list is stale. Refresh and try again.");
                }

                process.Kill();
                return OperationResult.Success;
            }
            catch (ArgumentException)
            {
                return OperationResult.Fail("The process has already exited.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        }, cancellationToken);

    public async Task<OperationResult> BlockAsync(string executable, CancellationToken cancellationToken = default)
    {
        if (!environment.IsElevated)
        {
            return OperationResult.Fail("Administrator rights are required to block applications.");
        }

        var exe = Normalize(executable);
        if (exe is null)
        {
            return OperationResult.Fail("Invalid executable name.");
        }

        var store = Load();
        lock (_gate)
        {
            if (store.ContainsKey(exe))
            {
                return OperationResult.Fail("This application is already blocked.");
            }
        }

        var keyPath = IfeoRoot + "\\" + exe;
        var previous = registry.TryRead("HKLM", keyPath, "Debugger", out var value) ? value.AsText() : string.Empty;
        var where = Path.Combine(Environment.SystemDirectory, "where.exe");
        registry.SetString("HKLM", keyPath, "Debugger", where, expand: false);

        if (!registry.TryRead("HKLM", keyPath, "Debugger", out var check) ||
            !string.Equals(check.AsText(), where, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail("The block could not be verified.");
        }

        store[exe] = new BlockRecord(previous, DateTimeOffset.Now);
        Save(store);

        try
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            {
                using (process)
                {
                    process.Kill();
                }
            }
        }
        catch (Exception)
        {
            // The block applies to the next launch regardless.
        }

        return await Task.FromResult(OperationResult.Success).ConfigureAwait(false);
    }

    public Task<OperationResult> UnblockAsync(string executable, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var exe = Normalize(executable);
            if (exe is null)
            {
                return OperationResult.Fail("Invalid executable name.");
            }

            var store = Load();
            BlockRecord? record;
            lock (_gate)
            {
                if (!store.TryGetValue(exe, out record))
                {
                    return OperationResult.Fail("This application is not in the block list.");
                }
            }

            var keyPath = IfeoRoot + "\\" + exe;
            if (!string.IsNullOrWhiteSpace(record.PreviousDebugger))
            {
                registry.SetString("HKLM", keyPath, "Debugger", record.PreviousDebugger, expand: false);
            }
            else
            {
                registry.DeleteValue("HKLM", keyPath, "Debugger");
            }

            lock (_gate)
            {
                store.Remove(exe);
            }
            Save(store);
            return OperationResult.Success;
        }, cancellationToken);

    public Task<IReadOnlyList<BlockedApplication>> ListBlockedAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<BlockedApplication>>(() =>
            Load().Select(kv => new BlockedApplication(kv.Key, kv.Value.BlockedAt, kv.Value.PreviousDebugger.Length > 0))
                .OrderByDescending(b => b.BlockedAt)
                .ToArray(), cancellationToken);

    private Dictionary<string, BlockRecord> Load() => JsonFile.Read<Dictionary<string, BlockRecord>>(_storePath, []);

    private void Save(Dictionary<string, BlockRecord> store) => JsonFile.Write(_storePath, store);

    private static string? Normalize(string executable)
    {
        var name = (executable ?? string.Empty).Trim().ToLowerInvariant();
        if (!name.EndsWith(".exe", StringComparison.Ordinal))
        {
            name += ".exe";
        }

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('.' or '_' or '-' or ' '))
            {
                return null;
            }
        }

        var bare = Path.GetFileNameWithoutExtension(name);
        return name.Length is > 4 and < 70 && !Protected.Contains(bare) ? name : null;
    }

    public sealed record BlockRecord(string PreviousDebugger, DateTimeOffset BlockedAt);
}
