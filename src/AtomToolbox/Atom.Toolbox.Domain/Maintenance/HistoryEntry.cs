namespace Atom.Toolbox.Domain.Maintenance;

/// <summary>
/// An audited change. Revert uses the catalog when the entry points at a tweak; ad-hoc
/// changes (power settings, startup items) carry an explicit revert payload instead.
/// </summary>
public sealed record HistoryEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required bool Applied { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Catalog page id for tweak entries; null for non-tweak entries.</summary>
    public string? PageId { get; init; }

    /// <summary>Startup item id, or a raw revert command for entries that cannot use the catalog.</summary>
    public string? RevertToken { get; init; }

    public string ActionLabel => Category switch
    {
        "startup" => Applied ? "DISABLED" : "ENABLED",
        _ => Applied ? "APPLIED" : "REVERTED"
    };
}
