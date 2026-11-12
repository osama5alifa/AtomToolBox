using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Infrastructure.Processes;
using Microsoft.Win32;

namespace Atom.Toolbox.Infrastructure.Apps;

/// <summary>
/// Desktop applications come from the native Uninstall registry keys (fast, no shell);
/// MSIX/Store packages come from a single Get-AppxPackage pass.
/// </summary>
public sealed partial class InstalledAppService(IProcessRunner processes) : IInstalledAppService
{
    private static readonly string[] UninstallRoots =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private readonly object _gate = new();
    private (DateTimeOffset Time, IReadOnlyList<InstalledApp> Apps)? _cache;

    public async Task<IReadOnlyList<InstalledApp>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_cache is { } cached && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromMinutes(2))
            {
                return cached.Apps;
            }
        }

        var apps = new List<InstalledApp>();
        apps.AddRange(ReadRegistryApps());
        apps.AddRange(await ReadStoreAppsAsync(cancellationToken).ConfigureAwait(false));

        var unique = apps
            .GroupBy(a => a.Name + "|" + a.Version, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        lock (_gate)
        {
            _cache = (DateTimeOffset.UtcNow, unique);
        }
        return unique;
    }

    public async Task<OperationResult> UninstallAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        Invalidate();
        if (app.IsWindowsApp)
        {
            if (string.IsNullOrWhiteSpace(app.PackageFullName))
            {
                return OperationResult.Fail("The package name is missing.");
            }

            var fullName = app.PackageFullName.Replace("'", "''");
            var script =
                "$ErrorActionPreference='Stop'; " +
                $"Get-AppxPackage -AllUsers | Where-Object {{ $_.PackageFullName -eq '{fullName}' }} | Remove-AppxPackage -AllUsers -ErrorAction Stop; " +
                "Write-Output 'REMOVED'";
            var result = await processes.RunAsync(PowerShell.Command(script, TimeSpan.FromMinutes(2)), cancellationToken)
                .ConfigureAwait(false);
            return result.Ok
                ? OperationResult.Success
                : OperationResult.Fail(FirstError(result) ?? "Windows could not remove the app.");
        }

        if (string.IsNullOrWhiteSpace(app.UninstallString))
        {
            return OperationResult.Fail("This application has no uninstall command.");
        }

        var command = app.UninstallString.Trim();
        if (command.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            var product = GuidPattern().Match(command);
            if (product.Success)
            {
                var result = await processes.RunAsync(new ProcessRequest(
                    "msiexec.exe",
                    ["/x", product.Value, "/norestart"],
                    Timeout: TimeSpan.FromMinutes(15)), cancellationToken).ConfigureAwait(false);
                return result.Ok
                    ? OperationResult.Success
                    : OperationResult.Fail(FirstError(result) ?? "The MSI uninstall failed.");
            }
        }

        var started = Process.Start(new ProcessStartInfo("cmd.exe", "/d /s /c \"" + command + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (started is null)
        {
            return OperationResult.Fail("The uninstaller could not be started.");
        }

        using (started)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            try
            {
                await started.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Fail("The uninstaller did not finish in time.");
            }

            return started.ExitCode == 0
                ? OperationResult.Success
                : OperationResult.Fail($"The uninstaller exited with code {started.ExitCode}.");
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cache = null;
        }
    }

    private static IEnumerable<InstalledApp> ReadRegistryApps()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var root in UninstallRoots)
                {
                    using var uninstall = baseKey.OpenSubKey(root);
                    if (uninstall is null)
                    {
                        continue;
                    }

                    foreach (var subName in uninstall.GetSubKeyNames())
                    {
                        using var entry = uninstall.OpenSubKey(subName);
                        if (entry is null)
                        {
                            continue;
                        }

                        var name = entry.GetValue("DisplayName")?.ToString()?.Trim();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        var systemComponent = entry.GetValue("SystemComponent");
                        if (systemComponent is int component && component == 1)
                        {
                            continue;
                        }

                        var size = entry.GetValue("EstimatedSize");
                        yield return new InstalledApp(
                            name,
                            entry.GetValue("Publisher")?.ToString()?.Trim() ?? "",
                            entry.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "",
                            size is int kb ? Math.Max(0, kb) : 0,
                            entry.GetValue("UninstallString")?.ToString() ?? "",
                            false,
                            null,
                            entry.GetValue("InstallLocation")?.ToString());
                    }
                }
            }
        }
    }

    private async Task<IReadOnlyList<InstalledApp>> ReadStoreAppsAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            Get-AppxPackage |
              Where-Object { $_.Name -notmatch 'Microsoft\.Windows\.|Microsoft\.VCLibs|Microsoft\.UI\.Xaml|Microsoft\.NET|Windows\.' } |
              Sort-Object Name |
              ForEach-Object {
                $display = $_.Name
                $manifest = Get-AppxPackageManifest -Package $_.PackageFullName -ErrorAction SilentlyContinue
                if ($manifest -and $manifest.Package.Properties.DisplayName -and $manifest.Package.Properties.DisplayName -notmatch '^ms-') {
                  $display = $manifest.Package.Properties.DisplayName
                }
                [PSCustomObject]@{
                  name = $display
                  publisher = $_.Publisher
                  version = [string]$_.Version
                  packageName = $_.PackageFullName
                  installLocation = $_.InstallLocation
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

        var apps = new List<InstalledApp>();
        foreach (var element in AsArray(document.RootElement))
        {
            var name = GetString(element, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            apps.Add(new InstalledApp(
                name,
                GetString(element, "publisher"),
                GetString(element, "version"),
                0,
                "",
                true,
                GetString(element, "packageName"),
                GetString(element, "installLocation")));
        }
        return apps;
    }

    private static IEnumerable<JsonElement> AsArray(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().ToArray(),
        JsonValueKind.Object => [element],
        _ => []
    };

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static string? FirstError(ProcessResult result)
    {
        var text = result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput;
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();
}
