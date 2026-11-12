using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Infrastructure.Registry;

namespace Atom.Toolbox.Infrastructure.Startup;

/// <summary>
/// Startup management using the same StartupApproved mechanism as Task Manager:
/// disabling never deletes a Run value, and startup-folder items are renamed instead
/// of moved. Every mutation is read back before it is reported.
/// </summary>
public sealed class StartupService(RegistryStore registry) : IStartupService
{
    private sealed record Store(string Id, string Hive, string RunPath, string ApprovedPath, string Label);

    private static readonly Store[] RegistryStores =
    [
        new("run-hkcu", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "HKCU RUN"),
        new("run-hklm", "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "HKLM RUN"),
        new("run-hkcu32", "HKCU", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", "HKCU RUN (32-BIT)"),
        new("run-hklm32", "HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", "HKLM RUN (32-BIT)")
    ];

    public Task<StartupScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(), cancellationToken);

    public Task<StartupChangeResult> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken cancellationToken = default) =>
        Task.Run(() => Set(entry, enabled), cancellationToken);

    private StartupScanResult Scan()
    {
        var items = new List<StartupEntry>();
        var stale = 0;

        foreach (var store in RegistryStores)
        {
            if (!registry.KeyExists(store.Hive, store.RunPath))
            {
                continue;
            }

            using var baseKey = store.Hive == "HKLM"
                ? Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                : Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64);
            using var runKey = baseKey.OpenSubKey(store.RunPath);
            if (runKey is null)
            {
                continue;
            }

            var names = runKey.GetValueNames().Where(n => n.Length > 0).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            using var approved = baseKey.OpenSubKey(store.ApprovedPath);
            foreach (var name in names)
            {
                var command = runKey.GetValue(name, string.Empty,
                    Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
                var disabled = IsDisabled(approved, name);
                items.Add(new StartupEntry($"{store.Id}|{name}", name, store.Label, StartupKind.Registry, !disabled, command));
            }

            if (approved is not null)
            {
                var runNames = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
                stale += approved.GetValueNames().Count(n => n.Length > 0 && !runNames.Contains(n));
            }
        }

        foreach (var (id, label, path) in FolderStores())
        {
            if (!Directory.Exists(path))
            {
                continue;
            }

            var files = Directory.GetFiles(path)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n) && !n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                .Cast<string>()
                .ToList();
            var names = files.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && file.Length > 9)
                {
                    var baseName = file[..^9];
                    if (names.Contains(baseName))
                    {
                        continue;
                    }

                    items.Add(new StartupEntry($"{id}|{baseName}", baseName, label, StartupKind.Folder, false,
                        Path.Combine(path, file)));
                }
                else
                {
                    items.Add(new StartupEntry($"{id}|{file}", file, label, StartupKind.Folder, true,
                        Path.Combine(path, file)));
                }
            }
        }

        return new StartupScanResult(items, stale);
    }

    private StartupChangeResult Set(StartupEntry entry, bool enabled)
    {
        var separator = entry.Id.IndexOf('|');
        if (separator <= 0 || separator == entry.Id.Length - 1)
        {
            return new StartupChangeResult(false, false, entry.IsEnabled, "Invalid startup item identifier.");
        }

        var storeId = entry.Id[..separator];
        var name = entry.Id[(separator + 1)..];

        if (entry.Kind == StartupKind.Registry)
        {
            return SetRegistry(storeId, name, enabled);
        }

        return SetFolder(storeId, name, enabled);
    }

    private StartupChangeResult SetRegistry(string storeId, string name, bool enabled)
    {
        var store = RegistryStores.FirstOrDefault(s => s.Id == storeId);
        if (store is null)
        {
            return new StartupChangeResult(false, false, !enabled, "Unknown startup source.");
        }

        if (!registry.ValueExists(store.Hive, store.RunPath, name))
        {
            return new StartupChangeResult(false, false, !enabled, "The startup entry no longer exists.");
        }

        if (enabled)
        {
            registry.DeleteValue(store.Hive, store.ApprovedPath, name);
            var present = TryReadApproval(store, name, out var first);
            var verified = !(present && first == 3);
            return new StartupChangeResult(true, verified, true, verified ? null : "The override value is still present.");
        }

        var time = DateTime.UtcNow.ToFileTimeUtc();
        var blob = new byte[12];
        blob[0] = 3;
        BitConverter.GetBytes(time).CopyTo(blob, 4);
        registry.SetBinary(store.Hive, store.ApprovedPath, name, blob);

        var nowPresent = TryReadApproval(store, name, out var nowFirst);
        var verifiedDisable = nowPresent && nowFirst == 3;
        return new StartupChangeResult(true, verifiedDisable, false, verifiedDisable ? null : "The disabled flag could not be verified.");
    }

    private StartupChangeResult SetFolder(string storeId, string name, bool enabled)
    {
        var (_, _, directory) = FolderStores().FirstOrDefault(s => s.Id == storeId);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new StartupChangeResult(false, false, !enabled, "The startup folder does not exist.");
        }

        var target = Path.Combine(directory, name);
        var disabled = target + ".disabled";

        if (enabled)
        {
            if (!File.Exists(disabled))
            {
                return File.Exists(target)
                    ? new StartupChangeResult(true, true, true, null)
                    : new StartupChangeResult(false, false, false, "The startup shortcut no longer exists.");
            }
            if (File.Exists(target))
            {
                return new StartupChangeResult(false, false, false, "A file with the original name already exists.");
            }

            File.Move(disabled, target);
            var verified = File.Exists(target) && !File.Exists(disabled);
            return new StartupChangeResult(true, verified, true, verified ? null : "The rename could not be verified.");
        }

        if (!File.Exists(target))
        {
            return File.Exists(disabled)
                ? new StartupChangeResult(true, true, false, null)
                : new StartupChangeResult(false, false, true, "The startup shortcut no longer exists.");
        }
        if (File.Exists(disabled))
        {
            return new StartupChangeResult(false, false, true, "A .disabled copy already exists.");
        }

        File.Move(target, disabled);
        var disabledVerified = !File.Exists(target) && File.Exists(disabled);
        return new StartupChangeResult(true, disabledVerified, false, disabledVerified ? null : "The rename could not be verified.");
    }

    private bool TryReadApproval(Store store, string name, out byte first)
    {
        first = 0;
        if (!registry.TryRead(store.Hive, store.ApprovedPath, name, out var value) || value.Data is not byte[] bytes || bytes.Length == 0)
        {
            return false;
        }
        first = bytes[0];
        return true;
    }

    private static bool IsDisabled(Microsoft.Win32.RegistryKey? approved, string name)
    {
        if (approved is null)
        {
            return false;
        }

        var value = approved.GetValue(name, null);
        return value switch
        {
            byte[] bytes when bytes.Length > 0 => bytes[0] == 3,
            object[] objects when objects.Length > 0 && objects[0] is byte b => b == 3,
            _ => false
        };
    }

    private static IEnumerable<(string Id, string Label, string Path)> FolderStores()
    {
        yield return ("folder-user", "STARTUP FOLDER (USER)",
            Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        yield return ("folder-common", "STARTUP FOLDER (ALL USERS)",
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
    }
}
