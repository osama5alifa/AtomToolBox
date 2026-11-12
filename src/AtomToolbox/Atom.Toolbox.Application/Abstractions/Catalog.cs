using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Domain.Power;
using Atom.Toolbox.Domain.Tweaks;

namespace Atom.Toolbox.Application.Abstractions;

public interface ITweakCatalog
{
    IReadOnlyList<TweakPage> Pages { get; }
    IReadOnlyList<TweakPage> FreePages { get; }
    IReadOnlyList<TweakPage> LockedPages { get; }
    TweakPage GetPage(string id);
    TweakDefinition? FindTweak(string pageId, string tweakId);
    IEnumerable<(TweakPage Page, TweakDefinition Tweak)> Search(string query);
    int TotalTweakCount { get; }
    int FreeTweakCount { get; }
}

public interface IAppCatalog
{
    IReadOnlyList<AppCatalogEntry> Apps { get; }
}

public interface IShortcutCatalog
{
    IReadOnlyList<ShortcutCategory> Categories { get; }
}

public interface IDebloatCatalog
{
    IReadOnlyList<DebloatCatalogEntry> Entries { get; }
}

public interface ICleanerCatalog
{
    IReadOnlyList<CleanerTask> Tasks { get; }
}

public interface IPowerPlanCatalog
{
    IReadOnlyList<BundledPowerPlan> Plans { get; }
    BundledPowerPlan? Find(string id);
}

public sealed record BundledPowerPlan(string Id, string Name, string Description, string ResourceName);
