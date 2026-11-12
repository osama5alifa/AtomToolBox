namespace Atom.Toolbox.Domain.Tweaks;

/// <summary>Stable action discriminators shared with the generated catalog.</summary>
public static class TweakActionKinds
{
    public const string Registry = "registry";
    public const string RegistryDelete = "registry-delete";
    public const string Service = "service";
    public const string PowerCfg = "powercfg";
    public const string PowerScheme = "power-scheme";
    public const string Process = "process";
    public const string PowerShell = "powershell";
    public const string Accent = "accent";
    public const string Batch = "batch";
    public const string Shell = "shell";
}

/// <summary>
/// One replayable operation produced by <c>Atom.CatalogBuilder</c>. Only the members that
/// belong to <see cref="Kind"/> are populated.
/// </summary>
public sealed class TweakAction
{
    public string Kind { get; init; } = "";
    public string? Hive { get; init; }
    public string? Path { get; init; }
    public string? Name { get; init; }
    public string? Type { get; init; }
    public string? Value { get; init; }
    public string? Service { get; init; }
    public string? Startup { get; init; }
    public string? Control { get; init; }
    public string? Executable { get; init; }
    public IReadOnlyList<string>? Arguments { get; init; }
    public string? Script { get; init; }
    public string? Color { get; init; }

    public bool WritesMachineState =>
        Kind is TweakActionKinds.Registry or TweakActionKinds.RegistryDelete &&
        !string.Equals(Hive, "HKCU", StringComparison.OrdinalIgnoreCase)
        || Kind is TweakActionKinds.Service or TweakActionKinds.PowerCfg or TweakActionKinds.PowerScheme;
}
