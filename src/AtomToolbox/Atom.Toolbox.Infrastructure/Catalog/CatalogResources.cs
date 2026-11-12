using System.Reflection;
using System.Text.Json;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Domain.Tweaks;

namespace Atom.Toolbox.Infrastructure.Catalog;

/// <summary>Loads every versioned data file that ships inside the assembly.</summary>
public static class CatalogResources
{
    private static readonly Lazy<TweakCatalog> TweakCatalog = new(LoadTweaks, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<IReadOnlyList<AppCatalogEntry>> AppCatalog = new(LoadApps, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<IReadOnlyList<DebloatCatalogEntry>> DebloatCatalog = new(LoadDebloat, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<IReadOnlyList<BundledPowerPlan>> PowerPlans = new(LoadPowerPlans, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<IReadOnlyList<ShortcutCategory>> Shortcuts = new(LoadShortcuts, LazyThreadSafetyMode.ExecutionAndPublication);

    public static TweakCatalog Tweaks => TweakCatalog.Value;
    public static IReadOnlyList<AppCatalogEntry> Apps => AppCatalog.Value;
    public static IReadOnlyList<DebloatCatalogEntry> Debloat => DebloatCatalog.Value;
    public static IReadOnlyList<BundledPowerPlan> Plans => PowerPlans.Value;
    public static IReadOnlyList<ShortcutCategory> ShortcutCategories => Shortcuts.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static byte[] ReadPowerPlan(string resourceName) => ReadBytes(resourceName);

    private static TweakCatalog LoadTweaks()
    {
        var json = ReadText("Catalog.tweaks.json");
        var document = JsonSerializer.Deserialize<CatalogDocument>(json, JsonOptions)
                       ?? throw new CatalogIntegrityException("The tweak catalog is empty.");
        if (document.Pages is null || document.Pages.Count == 0)
        {
            throw new CatalogIntegrityException("The tweak catalog contains no pages.");
        }

        return new TweakCatalog(document.Pages);
    }

    private static IReadOnlyList<AppCatalogEntry> LoadApps()
    {
        var items = JsonSerializer.Deserialize<List<AppDto>>(ReadText("Catalog.apps.json"), JsonOptions) ?? [];
        return items.Select(a => new AppCatalogEntry(
            a.Id, a.Name, a.Category, a.Url, a.Filename, a.Type ?? "exe_inno")).ToArray();
    }

    private static IReadOnlyList<DebloatCatalogEntry> LoadDebloat()
    {
        var items = JsonSerializer.Deserialize<List<DebloatDto>>(ReadText("Catalog.debloat.json"), JsonOptions) ?? [];
        return items
            .Where(a => !string.IsNullOrWhiteSpace(a.Id) && a.Names.Count > 0)
            .Select(a => new DebloatCatalogEntry(a.Id, a.Name, a.Category ?? "OTHER", a.Names, a.Description ?? ""))
            .ToArray();
    }

    private static IReadOnlyList<BundledPowerPlan> LoadPowerPlans()
    {
        var items = JsonSerializer.Deserialize<List<PowerPlanDto>>(ReadText("Catalog.powerplans.json"), JsonOptions) ?? [];
        var resources = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        return items.Select(p =>
        {
            var match = resources.FirstOrDefault(r =>
                r.EndsWith("." + p.Id + ".pow", StringComparison.OrdinalIgnoreCase));
            return match is null
                ? null
                : new BundledPowerPlan(p.Id, p.Name, p.Description ?? "", match);
        }).Where(p => p is not null).Cast<BundledPowerPlan>().ToArray();
    }

    private static IReadOnlyList<ShortcutCategory> LoadShortcuts()
    {
        var document = JsonSerializer.Deserialize<ShortcutDocument>(ReadText("Catalog.shortcuts.json"), JsonOptions);
        return document?.Categories
            .Select(c => new ShortcutCategory
            {
                Name = c.Name,
                Items = c.Items.Select(i => new ShortcutItem(i.Name, i.Cmd)).ToArray()
            })
            .ToArray() ?? [];
    }

    private static string ReadText(string suffix)
    {
        using var stream = Open(suffix);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(string resourceName)
    {
        using var stream = Open(resourceName, exact: true);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static Stream Open(string suffix, bool exact = false)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n =>
            exact ? n.Equals(suffix, StringComparison.OrdinalIgnoreCase) : n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            throw new CatalogIntegrityException($"Embedded resource missing: {suffix}");
        }
        return assembly.GetManifestResourceStream(name)
               ?? throw new CatalogIntegrityException($"Embedded resource unreadable: {suffix}");
    }

    private sealed class CatalogDocument
    {
        public string Version { get; init; } = "";
        public List<TweakPage> Pages { get; init; } = [];
    }

    private sealed class AppDto
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public string Url { get; init; } = "";
        public string Filename { get; init; } = "";
        public string? Type { get; init; }
    }

    private sealed class DebloatDto
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Category { get; init; }
        public List<string> Names { get; init; } = [];
        public string? Description { get; init; }
    }

    private sealed class PowerPlanDto
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Description { get; init; }
    }

    private sealed class ShortcutDocument
    {
        public List<ShortcutCategoryDto> Categories { get; init; } = [];
    }

    private sealed class ShortcutCategoryDto
    {
        public string Name { get; init; } = "";
        public List<ShortcutItemDto> Items { get; init; } = [];
    }

    private sealed class ShortcutItemDto
    {
        public string Name { get; init; } = "";
        public string Cmd { get; init; } = "";
    }
}

public sealed class TweakCatalog : ITweakCatalog
{
    private readonly Dictionary<string, TweakPage> _pages;
    private readonly Dictionary<string, Dictionary<string, TweakDefinition>> _index;

    public TweakCatalog(IReadOnlyList<TweakPage> pages)
    {
        Pages = pages;
        _pages = pages.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        _index = pages.ToDictionary(
            p => p.Id,
            p => p.Tweaks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        Validate();
    }

    public IReadOnlyList<TweakPage> Pages { get; }
    public IReadOnlyList<TweakPage> FreePages => Pages.Where(p => p.IsFree).ToArray();
    public IReadOnlyList<TweakPage> LockedPages => Pages.Where(p => !p.IsFree).ToArray();
    public int TotalTweakCount => Pages.Sum(p => p.Tweaks.Count);
    public int FreeTweakCount => FreePages.Sum(p => p.Tweaks.Count);

    public TweakPage GetPage(string id) =>
        _pages.TryGetValue(id, out var page) ? page : throw new KeyNotFoundException($"Unknown page '{id}'.");

    public TweakDefinition? FindTweak(string pageId, string tweakId) =>
        _index.TryGetValue(pageId, out var tweaks) && tweaks.TryGetValue(tweakId, out var tweak) ? tweak : null;

    public IEnumerable<(TweakPage Page, TweakDefinition Tweak)> Search(string query)
    {
        var term = query?.Trim() ?? string.Empty;
        if (term.Length == 0)
        {
            yield break;
        }

        foreach (var page in Pages)
        {
            foreach (var tweak in page.Tweaks)
            {
                if (tweak.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    tweak.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    tweak.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    page.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    yield return (page, tweak);
                }
            }
        }
    }

    private void Validate()
    {
        foreach (var page in Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Id) || string.IsNullOrWhiteSpace(page.Title))
            {
                throw new CatalogIntegrityException("A catalog page is missing its id or title.");
            }

            foreach (var tweak in page.Tweaks)
            {
                foreach (var action in tweak.Apply.Concat(tweak.Revert))
                {
                    if (!KnownKinds.Contains(action.Kind))
                    {
                        throw new CatalogIntegrityException($"Unknown action '{action.Kind}' in {page.Id}/{tweak.Id}.");
                    }
                }
            }
        }
    }

    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        TweakActionKinds.Registry,
        TweakActionKinds.RegistryDelete,
        TweakActionKinds.Service,
        TweakActionKinds.PowerCfg,
        TweakActionKinds.PowerScheme,
        TweakActionKinds.Process,
        TweakActionKinds.PowerShell,
        TweakActionKinds.Accent,
        TweakActionKinds.Batch,
        TweakActionKinds.Shell
    };
}
