using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Infrastructure.Catalog;
using Atom.Toolbox.Infrastructure.Processes;

namespace Atom.Toolbox.Infrastructure.Apps;

/// <summary>
/// AppX inventory and removal. Package names are validated against the shipped catalog and
/// never interpolated from UI input, which keeps the PowerShell bridge free from injection.
/// </summary>
public sealed partial class DebloatService(
    IProcessRunner processes,
    IDebloatCatalog catalog,
    IEnvironmentService environment) : IDebloatService
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan RemoveTimeout = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private (DateTimeOffset Time, DebloatScanResult Result)? _cache;

    public async Task<DebloatScanResult> ScanAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!force && _cache is { } cached && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromSeconds(60))
            {
                return cached.Result;
            }
        }

        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return new DebloatScanResult([], environment.IsElevated, true, "The AppX scan returned no readable result.");
        }

        var installedByName = snapshot.Installed
            .Where(p => p.Name.Length > 0)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var provisionedByName = snapshot.Provisioned
            .Where(p => p.Family.Length > 0)
            .GroupBy(p => p.Family, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var apps = new List<DebloatApp>();
        foreach (var entry in catalog.Entries)
        {
            var installed = entry.PackageNames
                .SelectMany(name => installedByName.TryGetValue(name, out var list) ? list : [])
                .ToList();
            var removable = installed.Count(p => !p.NonRemovable);
            var protectedCount = installed.Count(p => p.NonRemovable);
            var provisioned = entry.PackageNames.Sum(name => provisionedByName.GetValueOrDefault(name));

            apps.Add(new DebloatApp(
                entry.Id, entry.Name, entry.Category, entry.Description, entry.PackageNames,
                installed.Count, provisioned, protectedCount,
                removable > 0 || provisioned > 0));
        }

        var partial = snapshot.Scope != "all-users" || !string.IsNullOrWhiteSpace(snapshot.ProvisionedError);
        var result = new DebloatScanResult(apps, environment.IsElevated, partial, snapshot.ProvisionedError);

        lock (_gate)
        {
            _cache = (DateTimeOffset.UtcNow, result);
        }
        return result;
    }

    public async Task<DebloatRemovalResult> RemoveAsync(
        IReadOnlyList<string> ids,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsElevated)
        {
            return new DebloatRemovalResult(false, [], Error: "Administrator privileges are required to remove Windows apps.");
        }

        var selected = catalog.Entries.Where(e => ids.Contains(e.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0)
        {
            return new DebloatRemovalResult(false, [], Error: "No valid apps were selected.");
        }
        if (selected.Length > 100)
        {
            return new DebloatRemovalResult(false, [], Error: "Too many apps were selected.");
        }

        var names = selected
            .SelectMany(e => e.PackageNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Any(n => !PackageName().IsMatch(n)))
        {
            return new DebloatRemovalResult(false, [], Error: "The catalog contains an invalid package name.");
        }

        log?.Report($"Removing {selected.Length} app group(s) for all users…");
        var script = BuildRemovalScript(names);
        var run = await processes.RunAsync(PowerShell.Command(script, RemoveTimeout), cancellationToken).ConfigureAwait(false);
        if (run.TimedOut)
        {
            log?.Report("The removal helper timed out; verifying what was removed…");
        }

        await Task.Delay(900, cancellationToken).ConfigureAwait(false);
        var verification = await ScanAsync(force: true, cancellationToken).ConfigureAwait(false);
        var byId = verification.Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        var outcomes = new List<DebloatRemovalOutcome>();
        foreach (var entry in selected)
        {
            var remaining = byId.GetValueOrDefault(entry.Id);
            var errors = new List<string>();
            if (remaining is null)
            {
                errors.Add("The app could not be verified after removal.");
            }
            else
            {
                if (remaining.InstalledCount > 0)
                {
                    errors.Add($"{remaining.InstalledCount} installed package(s) are still present.");
                }
                if (remaining.ProvisionedCount > 0)
                {
                    errors.Add($"{remaining.ProvisionedCount} provisioned package(s) are still present.");
                }
            }

            outcomes.Add(new DebloatRemovalOutcome(
                entry.Id, entry.Name, errors.Count == 0, errors, errors));
            log?.Report($"{entry.Name}: {(errors.Count == 0 ? "removed" : string.Join(" ", errors))}");
        }

        var helperError = run.Ok || run.TimedOut ? null : FirstLine(run);
        return new DebloatRemovalResult(
            true,
            outcomes,
            Partial: verification.Partial,
            Error: helperError);
    }

    private async Task<Snapshot?> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            $scope = 'all-users'
            $installed = @()
            try {
              $installed = @(Get-AppxPackage -AllUsers -ErrorAction Stop)
            } catch {
              $scope = 'current-user'
              $installed = @(Get-AppxPackage)
            }
            $provisioned = @()
            $provisionedError = ''
            try {
              $provisioned = @(Get-AppxProvisionedPackage -Online -ErrorAction Stop)
            } catch {
              $provisionedError = $_.Exception.Message
            }
            [PSCustomObject]@{
              ok = $true
              scope = $scope
              provisionedError = $provisionedError
              installed = @($installed | ForEach-Object {
                [PSCustomObject]@{
                  name = $_.Name
                  packageFullName = $_.PackageFullName
                  version = [string]$_.Version
                  nonRemovable = [bool]$_.NonRemovable
                }
              })
              provisioned = @($provisioned | ForEach-Object {
                [PSCustomObject]@{
                  packageName = $_.PackageName
                  displayName = $_.DisplayName
                }
              })
            } | ConvertTo-Json -Compress -Depth 5
            """;

        var result = await processes.RunAsync(PowerShell.Command(script, ScanTimeout), cancellationToken).ConfigureAwait(false);
        using var document = PowerShell.ParseJson(result.StandardOutput);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var root = document.RootElement;
        var installed = new List<InstalledPackage>();
        if (root.TryGetProperty("installed", out var installedArray))
        {
            foreach (var item in Array(installedArray))
            {
                installed.Add(new InstalledPackage(
                    GetString(item, "name"),
                    GetString(item, "packageFullName"),
                    item.TryGetProperty("nonRemovable", out var nonRemovable) && nonRemovable.ValueKind == JsonValueKind.True));
            }
        }

        var provisioned = new List<ProvisionedPackage>();
        if (root.TryGetProperty("provisioned", out var provisionedArray))
        {
            foreach (var item in Array(provisionedArray))
            {
                var packageName = GetString(item, "packageName");
                provisioned.Add(new ProvisionedPackage(
                    packageName.Split('_')[0],
                    GetString(item, "displayName")));
            }
        }

        return new Snapshot(
            installed,
            provisioned,
            GetString(root, "scope") is { Length: > 0 } scope ? scope : "all-users",
            GetString(root, "provisionedError"));
    }

    private static string BuildRemovalScript(IReadOnlyList<string> names)
    {
        var quoted = string.Join(", ", names.Select(n => "'" + n.Replace("'", "''") + "'"));
        return $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $targets = @({{quoted}})
            $results = @()
            foreach ($n in $targets) {
              $removed = 0
              $errors = @()
              foreach ($p in @(Get-AppxPackage -AllUsers -Name $n -ErrorAction SilentlyContinue)) {
                try {
                  Remove-AppxPackage -Package $p.PackageFullName -AllUsers -ErrorAction Stop
                  $removed++
                } catch {
                  $errors += $_.Exception.Message
                }
              }
              $provisionedRemoved = 0
              foreach ($p in @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object { $_.PackageName.StartsWith($n + '_') })) {
                try {
                  Remove-AppxProvisionedPackage -Online -PackageName $p.PackageName -ErrorAction Stop
                  $provisionedRemoved++
                } catch {
                  $errors += $_.Exception.Message
                }
              }
              $results += [PSCustomObject]@{
                name = $n
                removed = $removed
                provisionedRemoved = $provisionedRemoved
                errors = @($errors)
              }
            }
            [PSCustomObject]@{ ok = $true; results = @($results) } | ConvertTo-Json -Compress -Depth 4
            """;
    }

    private static IEnumerable<JsonElement> Array(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().ToArray(),
        JsonValueKind.Object => [element],
        _ => []
    };

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? FirstLine(ProcessResult result)
    {
        var text = result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput;
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
    }

    private sealed record InstalledPackage(string Name, string PackageFullName, bool NonRemovable);

    private sealed record ProvisionedPackage(string Family, string DisplayName);

    private sealed record Snapshot(
        IReadOnlyList<InstalledPackage> Installed,
        IReadOnlyList<ProvisionedPackage> Provisioned,
        string Scope,
        string ProvisionedError);

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex PackageName();
}
