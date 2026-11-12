namespace Atom.Toolbox.Domain.Tweaks;

public sealed class TweakDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool DefaultState { get; init; }
    public bool RequiresAdmin { get; init; }
    public IReadOnlyList<TweakAction> Apply { get; init; } = Array.Empty<TweakAction>();
    public IReadOnlyList<TweakAction> Revert { get; init; } = Array.Empty<TweakAction>();
    public string? LegacyCheck { get; init; }

    /// <summary>
    /// Registry-only tweaks are verified natively by reading back every value that was written.
    /// Anything else falls back to the legacy PowerShell check command.
    /// </summary>
    public bool SupportsNativeVerification =>
        Apply.Count > 0 && Apply.All(a => a.Kind is TweakActionKinds.Registry or TweakActionKinds.RegistryDelete);
}

public sealed class TweakPage
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Tag { get; init; }
    public required string Access { get; init; }
    public IReadOnlyList<TweakDefinition> Tweaks { get; init; } = Array.Empty<TweakDefinition>();

    public bool IsFree => !string.Equals(Access, "pro", StringComparison.OrdinalIgnoreCase);
}

public enum TweakStatus
{
    Unknown,
    Off,
    On,
    Working,
    Failed
}

public sealed record TweakCheckResult(bool Enabled, string? Error = null)
{
    public static TweakCheckResult On { get; } = new(true);
    public static TweakCheckResult Off { get; } = new(false);
    public static TweakCheckResult Failed(string error) => new(false, error);
}
