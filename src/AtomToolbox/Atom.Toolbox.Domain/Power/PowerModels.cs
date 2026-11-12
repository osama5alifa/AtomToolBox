namespace Atom.Toolbox.Domain.Power;

public sealed record PowerPlan(string Guid, string Name, bool IsActive)
{
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    private static readonly HashSet<string> ProtectedGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        Balanced, HighPerformance, PowerSaver, UltimatePerformance
    };

    public bool IsBuiltIn => ProtectedGuids.Contains(Guid);
}

public sealed record PowerOption(int Index, string Name)
{
    public override string ToString() => Name;
}

public sealed class PowerSetting
{
    public required string Guid { get; init; }
    public required string Name { get; init; }
    public string? Alias { get; init; }
    public int? Ac { get; set; }
    public int? Dc { get; set; }
    public int? Min { get; init; }
    public int? Max { get; init; }
    public int? Increment { get; init; }
    public string? Units { get; init; }
    public bool IsHidden { get; set; }
    public IReadOnlyList<PowerOption> Options { get; init; } = Array.Empty<PowerOption>();
}

public sealed class PowerSubgroup
{
    public required string Guid { get; init; }
    public required string Name { get; init; }
    public List<PowerSetting> Settings { get; init; } = new();
}

public sealed record PowerSchemeDetails(
    string Guid,
    string? Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<PowerSubgroup> Subgroups,
    int SettingsCount,
    int HiddenCount)
{
    public PowerSetting? Find(string subgroupGuid, string settingGuid) =>
        Subgroups.FirstOrDefault(g => string.Equals(g.Guid, subgroupGuid, StringComparison.OrdinalIgnoreCase))
            ?.Settings.FirstOrDefault(s => string.Equals(s.Guid, settingGuid, StringComparison.OrdinalIgnoreCase));
}

public sealed record PowerChangeResult(
    bool Ok,
    bool Verified,
    bool Active,
    int? Ac,
    int? Dc,
    string? Error = null);

public sealed record TimerResolutionStatus(
    double CurrentMs,
    double FastestMs,
    double SlowestMs,
    bool IsHeld,
    double? HeldMs)
{
    public static TimerResolutionStatus Unavailable { get; } = new(0, 0, 0, false, null);
}
