using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Infrastructure.Catalog;

public sealed class AppCatalog : IAppCatalog
{
    public IReadOnlyList<AppCatalogEntry> Apps => CatalogResources.Apps;
}

public sealed class DebloatCatalog : IDebloatCatalog
{
    public IReadOnlyList<DebloatCatalogEntry> Entries => CatalogResources.Debloat;
}

public sealed class ShortcutCatalog : IShortcutCatalog
{
    public IReadOnlyList<ShortcutCategory> Categories => CatalogResources.ShortcutCategories;
}

public sealed class PowerPlanCatalog : IPowerPlanCatalog
{
    public IReadOnlyList<BundledPowerPlan> Plans => CatalogResources.Plans;

    public BundledPowerPlan? Find(string id) =>
        Plans.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
