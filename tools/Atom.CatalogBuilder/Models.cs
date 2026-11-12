using System.Text.Json.Serialization;

namespace Atom.CatalogBuilder;

internal sealed record CatalogFile(
    string Version,
    string GeneratedUtc,
    IReadOnlyList<CatalogPage> Pages);

internal sealed record CatalogPage(
    string Id,
    string Title,
    string Tag,
    string Access,
    IReadOnlyList<CatalogTweak> Tweaks);

internal sealed record CatalogTweak(
    string Id,
    string Name,
    string Description,
    bool DefaultState,
    bool RequiresAdmin,
    IReadOnlyList<TweakAction> Apply,
    IReadOnlyList<TweakAction> Revert,
    string? Check);

/// <summary>
/// One atomic, replayable operation. Only the fields relevant to <see cref="Kind"/> are set.
/// </summary>
internal sealed record TweakAction
{
    /// <summary>registry | registry-delete | service | powercfg | process | powershell | accent | batch</summary>
    public required string Kind { get; init; }

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
}

internal sealed record LegacyConfigTweak
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Script_on { get; init; }
    public string? Script_off { get; init; }
    public string? Command { get; init; }
    public string? Command_on { get; init; }
    public string? Command_off { get; init; }
    public bool Default_state { get; init; }
    public string? Check { get; init; }
}

internal sealed record ParseOutcome(
    IReadOnlyList<TweakAction> Actions,
    bool AdminRequired,
    string? FallbackReason);
