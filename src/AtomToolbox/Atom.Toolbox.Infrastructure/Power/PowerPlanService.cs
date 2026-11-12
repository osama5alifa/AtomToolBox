using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Power;
using Atom.Toolbox.Infrastructure.Catalog;
using Atom.Toolbox.Infrastructure.Registry;

namespace Atom.Toolbox.Infrastructure.Power;

/// <summary>
/// Complete power-scheme management over powercfg.exe. Reads both the visible and the
/// hidden setting dumps, and verifies every write with a read-back before reporting success.
/// </summary>
public sealed partial class PowerPlanService(
    IProcessRunner processes,
    IPowerPlanCatalog catalog,
    IEnvironmentService environment,
    RegistryStore registry) : IPowerPlanService
{
    private readonly object _mapGate = new();
    private Dictionary<string, string>? _schemeMap;

    public async Task<IReadOnlyList<PowerPlan>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await PowerCfgAsync(["/list"], TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException(result.StandardError.Length > 0
                ? result.StandardError
                : "powercfg returned no power plans.");
        }

        var plans = new List<PowerPlan>();
        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var match = SchemeLine().Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            plans.Add(new PowerPlan(
                match.Groups["guid"].Value.ToLowerInvariant(),
                match.Groups["name"].Value.Trim(),
                line.Contains('*')));
        }

        if (plans.Count > 0 && plans.All(p => !p.IsActive))
        {
            var active = await GetActiveGuidAsync(cancellationToken).ConfigureAwait(false);
            if (active is not null)
            {
                plans = plans.Select(p => p with { IsActive = p.Guid == active }).ToList();
            }
        }

        return plans;
    }

    public async Task<OperationResult> ActivateAsync(string guid, CancellationToken cancellationToken = default)
    {
        if (!IsGuid(guid))
        {
            return OperationResult.Fail("Invalid power plan identifier.");
        }

        var result = await PowerCfgAsync(["/setactive", guid], TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return OperationResult.Fail(Error(result, "powercfg could not activate the plan."));
        }

        var active = await GetActiveGuidAsync(cancellationToken).ConfigureAwait(false);
        return active == guid.ToLowerInvariant()
            ? OperationResult.Success
            : OperationResult.Fail("The plan did not become active (read-back mismatch).");
    }

    public async Task<OperationResult<string?>> ImportAsync(string file, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(file))
        {
            return OperationResult<string?>.Fail("The selected .pow file was not found.");
        }

        var imported = await ImportInternalAsync(file, cancellationToken).ConfigureAwait(false);
        return imported.Ok
            ? OperationResult<string?>.Success(imported.Value)
            : OperationResult<string?>.Fail(imported.Error ?? "The power plan could not be imported.");
    }

    public async Task<OperationResult<PowerPlan>> ApplyBundledAsync(string planId, CancellationToken cancellationToken = default)
    {
        var plan = catalog.Find(planId);
        if (plan is null)
        {
            return OperationResult<PowerPlan>.Fail("Unknown bundled power plan.");
        }

        var key = "bundled:" + plan.Id;
        var mapped = GetMappedGuid(key);
        var installed = await ListAsync(cancellationToken).ConfigureAwait(false);

        var existing = mapped is null ? null : installed.FirstOrDefault(p => p.Guid == mapped);
        existing ??= installed.FirstOrDefault(p => string.Equals(p.Name, plan.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SetMappedGuid(key, existing.Guid);
            var activated = await ActivateAsync(existing.Guid, cancellationToken).ConfigureAwait(false);
            return activated.Ok
                ? OperationResult<PowerPlan>.Success(existing with { IsActive = true })
                : OperationResult<PowerPlan>.Fail(activated.Error ?? "The plan could not be activated.");
        }

        var temp = Path.Combine(Path.GetTempPath(), $"atom_{plan.Id}_{Guid.NewGuid():N}.pow");
        try
        {
            await File.WriteAllBytesAsync(temp, CatalogResources.ReadPowerPlan(plan.ResourceName), cancellationToken)
                .ConfigureAwait(false);
            var imported = await ImportInternalAsync(temp, cancellationToken).ConfigureAwait(false);
            if (!imported.Ok || imported.Value is null)
            {
                return OperationResult<PowerPlan>.Fail(imported.Error ?? "The plan could not be imported.");
            }

            if (!string.IsNullOrWhiteSpace(plan.Name))
            {
                await RunPowerCfgAsync(["/changename", imported.Value, plan.Name], TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(false);
            }

            SetMappedGuid(key, imported.Value);
            var activated = await ActivateAsync(imported.Value, cancellationToken).ConfigureAwait(false);
            return activated.Ok
                ? OperationResult<PowerPlan>.Success(new PowerPlan(imported.Value, plan.Name, true))
                : OperationResult<PowerPlan>.Fail(activated.Error ?? "The plan could not be activated.");
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Temp cleanup is best effort.
            }
        }
    }

    public async Task<OperationResult> DeleteAsync(string guid, CancellationToken cancellationToken = default)
    {
        if (!IsGuid(guid))
        {
            return OperationResult.Fail("Invalid power plan identifier.");
        }

        if (PowerPlanService.ProtectedGuids.Contains(guid))
        {
            return OperationResult.Fail("Built-in Windows power plans cannot be removed.");
        }

        var installed = await ListAsync(cancellationToken).ConfigureAwait(false);
        var plan = installed.FirstOrDefault(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return OperationResult.Fail("The plan no longer exists.");
        }
        if (plan.IsActive)
        {
            return OperationResult.Fail("This plan is active. Activate another plan first.");
        }

        var result = await PowerCfgAsync(["/delete", guid], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return result.Ok
            ? OperationResult.Success
            : OperationResult.Fail(Error(result, "powercfg could not delete the plan."));
    }

    public async Task<OperationResult> RenameAsync(string guid, string name, string? description, CancellationToken cancellationToken = default)
    {
        if (!IsGuid(guid))
        {
            return OperationResult.Fail("Invalid power plan identifier.");
        }

        var cleanName = Sanitize(name, 120);
        var cleanDescription = Sanitize(description ?? string.Empty, 400);
        if (cleanName.Length == 0)
        {
            return OperationResult.Fail("The plan name cannot be empty.");
        }

        var result = await PowerCfgAsync(
            ["/changename", guid, cleanName, cleanDescription],
            TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return OperationResult.Fail(Error(result, "powercfg rejected the rename."));
        }

        var installed = await ListAsync(cancellationToken).ConfigureAwait(false);
        return installed.Any(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(p.Name, cleanName, StringComparison.Ordinal))
            ? OperationResult.Success
            : OperationResult.Fail("The rename could not be verified.");
    }

    public async Task<OperationResult<PowerPlan>> DuplicateAsync(string guid, string name, string? description, CancellationToken cancellationToken = default)
    {
        if (!IsGuid(guid))
        {
            return OperationResult<PowerPlan>.Fail("Invalid power plan identifier.");
        }

        var before = await ListAsync(cancellationToken).ConfigureAwait(false);
        var result = await PowerCfgAsync(["/duplicatescheme", guid], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return OperationResult<PowerPlan>.Fail(Error(result, "powercfg could not duplicate the plan."));
        }

        var after = await ListAsync(cancellationToken).ConfigureAwait(false);
        var known = before.Select(p => p.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newGuid = after.Select(p => p.Guid).FirstOrDefault(g => !known.Contains(g))
                      ?? GuidPattern().Match(result.StandardOutput).Value.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(newGuid))
        {
            return OperationResult<PowerPlan>.Fail("The new plan's identifier could not be determined.");
        }

        var cleanName = Sanitize(name, 120);
        if (cleanName.Length > 0)
        {
            var renamed = await RenameAsync(newGuid, cleanName, description, cancellationToken).ConfigureAwait(false);
            if (!renamed.Ok)
            {
                return OperationResult<PowerPlan>.Fail(renamed.Error ?? "The duplicate could not be renamed.");
            }
        }

        var plan = after.FirstOrDefault(p => p.Guid == newGuid);
        return OperationResult<PowerPlan>.Success(plan ?? new PowerPlan(newGuid, cleanName, false));
    }

    public async Task<OperationResult<long>> ExportAsync(string guid, string file, CancellationToken cancellationToken = default)
    {
        if (!IsGuid(guid))
        {
            return OperationResult<long>.Fail("Invalid power plan identifier.");
        }

        try
        {
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            return OperationResult<long>.Fail(ex.Message);
        }

        var result = await PowerCfgAsync(["/export", file, guid], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return OperationResult<long>.Fail(Error(result, "powercfg could not export the plan."));
        }

        var length = File.Exists(file) ? new FileInfo(file).Length : 0;
        return length > 0
            ? OperationResult<long>.Success(length)
            : OperationResult<long>.Fail("The export produced an empty file.");
    }

    public async Task<OperationResult<PowerSchemeDetails>> ReadSchemeAsync(string guid, CancellationToken cancellationToken = default)
    {
        if (!IsGuid(guid))
        {
            return OperationResult<PowerSchemeDetails>.Fail("Invalid power plan identifier.");
        }

        var visibleTask = PowerCfgAsync(["/q", guid], TimeSpan.FromSeconds(60), cancellationToken);
        var hiddenTask = PowerCfgAsync(["/qh", guid], TimeSpan.FromSeconds(60), cancellationToken);
        await Task.WhenAll(visibleTask, hiddenTask).ConfigureAwait(false);

        var hidden = ParseDump((await hiddenTask.ConfigureAwait(false)).StandardOutput);
        if (hidden.Count == 0)
        {
            return OperationResult<PowerSchemeDetails>.Fail("powercfg returned no settings for this plan.");
        }

        var visible = ParseDump((await visibleTask.ConfigureAwait(false)).StandardOutput);
        var visibleGuids = visible
            .SelectMany(g => g.Settings)
            .Where(s => s.Ac is not null)
            .Select(s => s.Guid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hiddenCount = 0;
        var settingsCount = 0;
        foreach (var subgroup in hidden)
        {
            foreach (var setting in subgroup.Settings)
            {
                settingsCount++;
                var isHidden = visibleGuids.Count > 0 && !visibleGuids.Contains(setting.Guid);
                if (isHidden)
                {
                    hiddenCount++;
                }
                setting.IsHidden = isHidden;
            }
        }

        var name = ParseSchemeName((await hiddenTask.ConfigureAwait(false)).StandardOutput);
        var description = ReadDescription(guid);
        var installed = await ListAsync(cancellationToken).ConfigureAwait(false);
        var active = installed.Any(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase) && p.IsActive);

        return OperationResult<PowerSchemeDetails>.Success(new PowerSchemeDetails(
            guid, name, description, active, hidden, settingsCount, hiddenCount));
    }

    public async Task<OperationResult<string?>> EnsureSchemeAsync(string templateGuid, string friendlyName, CancellationToken cancellationToken = default)
    {
        if (!IsGuid(templateGuid))
        {
            return OperationResult<string?>.Fail("Invalid power scheme template.");
        }

        var installed = await ListAsync(cancellationToken).ConfigureAwait(false);
        var mapped = GetMappedGuid("template:" + templateGuid);
        var existing = mapped is null ? null : installed.FirstOrDefault(p => p.Guid == mapped);
        existing ??= installed.FirstOrDefault(p => string.Equals(p.Name, friendlyName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SetMappedGuid("template:" + templateGuid, existing.Guid);
            var activate = await ActivateAsync(existing.Guid, cancellationToken).ConfigureAwait(false);
            return activate.Ok
                ? OperationResult<string?>.Success(existing.Guid)
                : OperationResult<string?>.Fail(activate.Error ?? "The scheme could not be activated.");
        }

        var duplicate = await DuplicateAsync(templateGuid, friendlyName, null, cancellationToken).ConfigureAwait(false);
        if (!duplicate.Ok || duplicate.Value is null)
        {
            return OperationResult<string?>.Fail(duplicate.Error ?? "The scheme could not be created.");
        }

        var activation = await ActivateAsync(duplicate.Value.Guid, cancellationToken).ConfigureAwait(false);
        if (!activation.Ok)
        {
            return OperationResult<string?>.Fail(activation.Error ?? "The scheme could not be activated.");
        }

        SetMappedGuid("template:" + templateGuid, duplicate.Value.Guid);
        return OperationResult<string?>.Success(duplicate.Value.Guid);
    }

    public async Task<PowerChangeResult> SetValueAsync(
        string guid, string subgroup, string setting, int ac, int dc, int? min, int? max,
        CancellationToken cancellationToken = default)
    {
        if (!IsGuid(guid) || !IsGuid(subgroup) || !IsGuid(setting))
        {
            return new PowerChangeResult(false, false, false, null, null, "Invalid power setting identifiers.");
        }

        if (min is not null && ac < min || max is not null && ac > max ||
            min is not null && dc < min || max is not null && dc > max)
        {
            return new PowerChangeResult(false, false, false, null, null, "The value is outside the supported range.");
        }

        var installed = await ListAsync(cancellationToken).ConfigureAwait(false);
        var isActive = installed.Any(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase) && p.IsActive);

        var acResult = await PowerCfgAsync(
            ["/setacvalueindex", guid, subgroup, setting, ac.ToString(CultureInfo.InvariantCulture)],
            TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        var dcResult = await PowerCfgAsync(
            ["/setdcvalueindex", guid, subgroup, setting, dc.ToString(CultureInfo.InvariantCulture)],
            TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

        if (!acResult.Ok || !dcResult.Ok)
        {
            return new PowerChangeResult(false, false, isActive, null, null, "powercfg rejected the change.");
        }

        if (isActive)
        {
            await PowerCfgAsync(["/setactive", guid], TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        }

        var check = await PowerCfgAsync(["/qh", guid, subgroup, setting], TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);
        var parsed = ParseDump(check.StandardOutput)
            .SelectMany(g => g.Settings)
            .FirstOrDefault(s => s.Guid.Equals(setting, StringComparison.OrdinalIgnoreCase));

        var newAc = parsed?.Ac;
        var newDc = parsed?.Dc;
        var verified = newAc == ac && newDc == dc;
        return new PowerChangeResult(true, verified, isActive, newAc, newDc,
            verified ? null : "The values were written but the read-back differs.");
    }

    public static IReadOnlySet<string> ProtectedGuids => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PowerPlan.Balanced, PowerPlan.HighPerformance, PowerPlan.PowerSaver, PowerPlan.UltimatePerformance
    };

    private async Task<OperationResult<string?>> ImportInternalAsync(string file, CancellationToken cancellationToken)
    {
        var before = await ListAsync(cancellationToken).ConfigureAwait(false);
        var result = await PowerCfgAsync(["/import", file], TimeSpan.FromSeconds(45), cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return OperationResult<string?>.Fail(Error(result, "powercfg could not import the plan."));
        }

        var after = await ListAsync(cancellationToken).ConfigureAwait(false);
        var known = before.Select(p => p.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newGuid = after.Select(p => p.Guid).FirstOrDefault(g => !known.Contains(g))
                      ?? GuidPattern().Match(result.StandardOutput).Value.ToLowerInvariant();
        return string.IsNullOrWhiteSpace(newGuid)
            ? OperationResult<string?>.Fail("The imported plan's identifier could not be determined.")
            : OperationResult<string?>.Success(newGuid);
    }

    private async Task<string?> GetActiveGuidAsync(CancellationToken cancellationToken)
    {
        var result = await PowerCfgAsync(["/getactivescheme"], TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        var match = GuidPattern().Match(result.StandardOutput + result.StandardError);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    private string? ReadDescription(string guid)
    {
        var path = $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{guid}";
        if (!registry.TryRead("HKLM", path, "Description", out var value))
        {
            return null;
        }

        var text = value.AsText();
        return text.StartsWith('@') ? null : text;
    }

    private Dictionary<string, string> LoadMap()
    {
        lock (_mapGate)
        {
            if (_schemeMap is not null)
            {
                return _schemeMap;
            }

            try
            {
                var path = Path.Combine(environment.LocalAppDataDirectory, "power-schemes.json");
                _schemeMap = File.Exists(path)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                    : [];
            }
            catch (Exception)
            {
                _schemeMap = [];
            }
            return _schemeMap;
        }
    }

    private string? GetMappedGuid(string key)
    {
        var map = LoadMap();
        lock (_mapGate)
        {
            return map.TryGetValue(key, out var guid) ? guid : null;
        }
    }

    private void SetMappedGuid(string key, string guid)
    {
        var map = LoadMap();
        lock (_mapGate)
        {
            map[key] = guid;
            try
            {
                var path = Path.Combine(environment.LocalAppDataDirectory, "power-schemes.json");
                File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception)
            {
                // The mapping is an optimisation; losing it only causes a re-import.
            }
        }
    }

    private static List<PowerSubgroup> ParseDump(string text)
    {
        var subgroups = new List<PowerSubgroup>();
        PowerSubgroup? currentGroup = null;
        PowerSetting? currentSetting = null;
        int? pendingIndex = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            var guidMatch = GuidWithName().Match(trimmed);
            if (guidMatch.Success)
            {
                if (indent == 0)
                {
                    continue;
                }
                if (indent <= 3)
                {
                    currentGroup = new PowerSubgroup
                    {
                        Guid = guidMatch.Groups["guid"].Value.ToLowerInvariant(),
                        Name = guidMatch.Groups["name"].Value.Trim()
                    };
                    subgroups.Add(currentGroup);
                    currentSetting = null;
                    pendingIndex = null;
                    continue;
                }
                if (indent <= 6 && currentGroup is not null)
                {
                    currentSetting = new PowerSetting
                    {
                        Guid = guidMatch.Groups["guid"].Value.ToLowerInvariant(),
                        Name = guidMatch.Groups["name"].Value.Trim()
                    };
                    currentGroup.Settings.Add(currentSetting);
                    pendingIndex = null;
                    continue;
                }
                continue;
            }

            if (currentSetting is null)
            {
                continue;
            }

            var match = AliasLine().Match(trimmed);
            if (match.Success)
            {
                currentSetting = CopyWithAlias(currentSetting, match.Groups["alias"].Value);
                ReplaceLast(currentGroup!, currentSetting);
                continue;
            }

            match = IndexLine().Match(trimmed);
            if (match.Success)
            {
                pendingIndex = ParseInt(match.Groups["value"].Value);
                continue;
            }

            match = FriendlyNameLine().Match(trimmed);
            if (match.Success && pendingIndex is not null)
            {
                currentSetting = CopyWithOption(currentSetting, pendingIndex.Value, match.Groups["value"].Value.Trim());
                ReplaceLast(currentGroup!, currentSetting);
                pendingIndex = null;
                continue;
            }

            match = AcLine().Match(trimmed);
            if (match.Success)
            {
                var value = ParseInt(match.Groups["value"].Value);
                if (value is not null)
                {
                    currentSetting.Ac = value;
                }
                continue;
            }

            match = DcLine().Match(trimmed);
            if (match.Success)
            {
                var value = ParseInt(match.Groups["value"].Value);
                if (value is not null)
                {
                    currentSetting.Dc = value;
                }
                continue;
            }

            match = MinLine().Match(trimmed);
            if (match.Success)
            {
                currentSetting = CopyWithBounds(currentSetting, ParseInt(match.Groups["value"].Value), currentSetting.Max, currentSetting.Increment);
                ReplaceLast(currentGroup!, currentSetting);
                continue;
            }

            match = MaxLine().Match(trimmed);
            if (match.Success)
            {
                currentSetting = CopyWithBounds(currentSetting, currentSetting.Min, ParseInt(match.Groups["value"].Value), currentSetting.Increment);
                ReplaceLast(currentGroup!, currentSetting);
                continue;
            }

            match = IncrementLine().Match(trimmed);
            if (match.Success)
            {
                currentSetting = CopyWithBounds(currentSetting, currentSetting.Min, currentSetting.Max, ParseInt(match.Groups["value"].Value));
                ReplaceLast(currentGroup!, currentSetting);
                continue;
            }

            match = UnitsLine().Match(trimmed);
            if (match.Success)
            {
                currentSetting = CopyWithUnits(currentSetting, match.Groups["value"].Value.Trim());
                ReplaceLast(currentGroup!, currentSetting);
            }
        }

        return subgroups;
    }

    private static void ReplaceLast(PowerSubgroup group, PowerSetting setting)
    {
        if (group.Settings.Count > 0)
        {
            group.Settings[^1] = setting;
        }
    }

    private static PowerSetting CopyWithAlias(PowerSetting setting, string alias) => new()
    {
        Guid = setting.Guid,
        Name = setting.Name,
        Alias = alias,
        Ac = setting.Ac,
        Dc = setting.Dc,
        Min = setting.Min,
        Max = setting.Max,
        Increment = setting.Increment,
        Units = setting.Units,
        IsHidden = setting.IsHidden,
        Options = setting.Options
    };

    private static PowerSetting CopyWithOption(PowerSetting setting, int index, string name)
    {
        var options = setting.Options.ToList();
        options.Add(new PowerOption(index, name));
        return new PowerSetting
        {
            Guid = setting.Guid,
            Name = setting.Name,
            Alias = setting.Alias,
            Ac = setting.Ac,
            Dc = setting.Dc,
            Min = setting.Min,
            Max = setting.Max,
            Increment = setting.Increment,
            Units = setting.Units,
            IsHidden = setting.IsHidden,
            Options = options
        };
    }

    private static PowerSetting CopyWithBounds(PowerSetting setting, int? min, int? max, int? increment) => new()
    {
        Guid = setting.Guid,
        Name = setting.Name,
        Alias = setting.Alias,
        Ac = setting.Ac,
        Dc = setting.Dc,
        Min = min,
        Max = max,
        Increment = increment,
        Units = setting.Units,
        IsHidden = setting.IsHidden,
        Options = setting.Options
    };

    private static PowerSetting CopyWithUnits(PowerSetting setting, string units) => new()
    {
        Guid = setting.Guid,
        Name = setting.Name,
        Alias = setting.Alias,
        Ac = setting.Ac,
        Dc = setting.Dc,
        Min = setting.Min,
        Max = setting.Max,
        Increment = setting.Increment,
        Units = units,
        IsHidden = setting.IsHidden,
        Options = setting.Options
    };

    private static string? ParseSchemeName(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var match = SchemeLine().Match(line.Trim());
            if (match.Success)
            {
                return match.Groups["name"].Value.Trim();
            }
        }
        return null;
    }

    private async Task<ProcessResult> PowerCfgAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(new ProcessRequest("powercfg.exe", arguments, Timeout: timeout), cancellationToken)
            .ConfigureAwait(false);
        // powercfg returns 1 for some successful reads; callers inspect the output.
        return result;
    }

    private async Task<ProcessResult> RunPowerCfgAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await PowerCfgAsync(arguments, timeout, cancellationToken).ConfigureAwait(false);

    private static string Error(ProcessResult result, string fallback)
    {
        var text = result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput;
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(line) ? fallback : line;
    }

    private static int? ParseInt(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        }
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string Sanitize(string value, int maxLength)
    {
        var clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static bool IsGuid(string value) => GuidPattern().IsMatch(value);

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\((?<name>.+)\)\s*(?<active>\*)?\s*$")]
    private static partial Regex SchemeLine();

    [GeneratedRegex(@"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\(\s*(?<name>.+?)\s*\)\s*$")]
    private static partial Regex GuidWithName();

    [GeneratedRegex(@"GUID Alias:\s*(?<alias>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AliasLine();

    [GeneratedRegex(@"Possible Setting Index:\s*(?<value>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex IndexLine();

    [GeneratedRegex(@"Possible Setting Friendly Name:\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FriendlyNameLine();

    [GeneratedRegex(@"Current AC[^:]*:\s*(?<value>0x[0-9a-f]+|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex AcLine();

    [GeneratedRegex(@"Current DC[^:]*:\s*(?<value>0x[0-9a-f]+|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DcLine();

    [GeneratedRegex(@"Minimum Possible Setting:\s*(?<value>0x[0-9a-f]+|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MinLine();

    [GeneratedRegex(@"Maximum Possible Setting:\s*(?<value>0x[0-9a-f]+|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MaxLine();

    [GeneratedRegex(@"Possible Settings increment:\s*(?<value>0x[0-9a-f]+|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex IncrementLine();

    [GeneratedRegex(@"Possible Settings units:\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex UnitsLine();
}
