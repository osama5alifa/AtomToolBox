using System.Text.Json;
using System.Text.RegularExpressions;

namespace Atom.CatalogBuilder;

internal static class Program
{
    private sealed record PageDefinition(string Id, string Title, string Tag, string Config, string Access);

    private static readonly PageDefinition[] Pages =
    [
        new("windows", "WINDOWS & VISUAL OPTIMIZATIONS", "WINDOWS", "windows", "free"),
        new("gaming", "GAMING OPTIMIZATIONS", "GAMING", "gaming", "free"),
        new("gpu", "GPU CONFIGURATIONS", "GPU", "gpu", "free"),
        new("power", "POWER & USB MANAGEMENT", "POWER", "power", "free"),
        new("network", "NETWORK & LATENCY", "NETWORK", "network", "free"),
        new("privacy", "PRIVACY SETTINGS", "PRIVACY", "privacy", "free"),
        new("personalize", "PERSONALIZE & APPEARANCE", "THEME", "personalize", "free"),
        new("brave", "BRAVE BROWSER DEBLOAT", "BRAVE", "brave", "free"),
        new("others", "SYSTEM TOOLKIT", "TOOLKIT", "others", "pro"),
        new("winupdate", "WINDOWS UPDATE", "UPDATES", "winupdate", "pro")
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var root = ResolveRepositoryRoot(args);
        var legacy = ResolveLegacyRoot(args, root);
        if (!Directory.Exists(legacy))
        {
            Console.Error.WriteLine($"Legacy source not found: {legacy}");
            Console.Error.WriteLine("Pass --legacy <path> or set ATOM_LEGACY_ROOT to a clone of the 0.2.0 Electron repository.");
            return 2;
        }

        var scriptsRoot = Path.Combine(legacy, "src", "scripts");
        var configRoot = Path.Combine(legacy, "src", "config");
        var outputRoot = Path.Combine(root, "src", "AtomToolbox", "Atom.Toolbox.Infrastructure", "Catalog");
        Directory.CreateDirectory(outputRoot);

        var report = new List<string> { "AtomToolBox catalog build report", $"Source: {legacy}", "" };
        var pages = new List<CatalogPage>();
        var fallbacks = new List<string>();

        foreach (var pageDefinition in Pages)
        {
            var configPath = Path.Combine(configRoot, pageDefinition.Config + ".json");
            var tweaks = JsonSerializer.Deserialize<List<LegacyConfigTweak>>(File.ReadAllText(configPath), JsonOptions)
                         ?? throw new InvalidOperationException($"Could not read {configPath}");

            var built = new List<CatalogTweak>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tweak in tweaks)
            {
                if (!seen.Add(tweak.Id))
                {
                    throw new InvalidOperationException($"Duplicate tweak id '{tweak.Id}' in {pageDefinition.Config}.");
                }

                var applyText = ResolveScript(tweak.Script_on, scriptsRoot) ?? tweak.Command_on ?? tweak.Command ?? string.Empty;
                var revertText = ResolveScript(tweak.Script_off, scriptsRoot) ?? tweak.Command_off ?? tweak.Command ?? string.Empty;

                var on = string.IsNullOrWhiteSpace(applyText)
                    ? new ParseOutcome(Array.Empty<TweakAction>(), false, null)
                    : ScriptParser.Parse(applyText, tweak.Id);

                // powercfg cannot "ensure a scheme exists"; it always duplicates. The legacy
                // script wrapped it in an existence check. Encode that as a first-class action.
                if (tweak.Id == "ultimate_power" && on.FallbackReason is not null)
                {
                    on = new ParseOutcome(
                        new[]
                        {
                            new TweakAction
                            {
                                Kind = "power-scheme",
                                Arguments = new[] { "e9a42b02-d5df-448d-aa00-03f14749eb61" }
                            }
                        },
                        true,
                        null);
                }

                var off = string.IsNullOrWhiteSpace(revertText)
                    ? new ParseOutcome(Array.Empty<TweakAction>(), false, null)
                    : ScriptParser.Parse(revertText, tweak.Id);

                if (on.FallbackReason is not null)
                {
                    fallbacks.Add($"{pageDefinition.Id}/{tweak.Id}: ON → {on.FallbackReason}");
                }
                if (off.FallbackReason is not null)
                {
                    fallbacks.Add($"{pageDefinition.Id}/{tweak.Id}: OFF → {off.FallbackReason}");
                }

                built.Add(new CatalogTweak(
                    tweak.Id,
                    tweak.Name,
                    tweak.Description,
                    tweak.Default_state,
                    on.AdminRequired || off.AdminRequired,
                    on.Actions,
                    off.Actions,
                    string.IsNullOrWhiteSpace(tweak.Check) ? null : tweak.Check));
            }

            pages.Add(new CatalogPage(pageDefinition.Id, pageDefinition.Title, pageDefinition.Tag, pageDefinition.Access, built));
            report.Add($"{pageDefinition.Id,-12} {built.Count,3} tweaks  ({pageDefinition.Access})");
        }

        var catalog = new CatalogFile("0.2.5", DateTime.UtcNow.ToString("O"), pages);
        var catalogPath = Path.Combine(outputRoot, "tweaks.json");
        WriteJson(catalogPath, catalog);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(catalogPath)));
        File.WriteAllText(Path.Combine(outputRoot, "catalog.sha256"), hash);

        foreach (var file in new[] { "apps.json", "debloat.json", "powerplans.json" })
        {
            var source = Path.Combine(configRoot, file);
            File.Copy(source, Path.Combine(outputRoot, file), overwrite: true);
        }

        BuildShortcuts(legacy, outputRoot);
        CopyPowerPlans(legacy, outputRoot);
        CopyHelperScripts(scriptsRoot, Path.Combine(root, "src", "AtomToolbox", "Atom.Toolbox.Infrastructure", "Assets", "Scripts"));

        report.Add("");
        report.Add($"TOTAL: {pages.Sum(p => p.Tweaks.Count)} tweaks");
        report.Add("");
        report.Add("Native action coverage:");
        var allActions = pages.SelectMany(p => p.Tweaks).SelectMany(t => t.Apply.Concat(t.Revert)).ToList();
        foreach (var group in allActions.GroupBy(a => a.Kind).OrderByDescending(g => g.Count()))
        {
            report.Add($"  {group.Key,-16} {group.Count(),4}");
        }
        report.Add("");
        report.Add($"Batch fallbacks: {fallbacks.Count}");
        report.AddRange(fallbacks.Select(f => "  " + f));

        var reportPath = Path.Combine(root, "tools", "catalog-report.txt");
        File.WriteAllLines(reportPath, report);
        Console.WriteLine(string.Join(Environment.NewLine, report));
        return 0;
    }

    private static string ResolveRepositoryRoot(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--root" or "-r")
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AtomToolbox.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (AtomToolbox.sln).");
    }

    private static string ResolveLegacyRoot(string[] args, string root)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--legacy" or "-l")
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        var configured = Environment.GetEnvironmentVariable("ATOM_LEGACY_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.GetFullPath(Path.Combine(root, "..", "AtomToolBox-legacy"));
    }

    private static string? ResolveScript(string? relative, string scriptsRoot)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var path = Path.Combine(scriptsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Script referenced by config is missing: {path}");
        }
        return File.ReadAllText(path);
    }

    private static void BuildShortcuts(string legacy, string outputRoot)
    {
        var renderer = File.ReadAllText(Path.Combine(legacy, "src", "renderer.js"));
        var start = renderer.IndexOf("const shortcutCategories = [", StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("shortcutCategories not found in renderer.js");
        }

        var end = renderer.IndexOf("\n];", start, StringComparison.Ordinal);
        var block = renderer[start..end];

        var categories = new List<object>();
        var categoryRegex = new Regex(@"\{\s*name:\s*'(?<name>[^']*)',\s*items:\s*\[(?<items>.*?)\]\s*\}", RegexOptions.Singleline);
        var itemRegex = new Regex(@"\{\s*name:\s*'(?<name>[^']*)',\s*cmd:\s*'(?<cmd>[^']*)'\s*\}");

        foreach (Match category in categoryRegex.Matches(block))
        {
            var items = itemRegex.Matches(category.Groups["items"].Value)
                .Select(m => new { name = m.Groups["name"].Value, cmd = m.Groups["cmd"].Value })
                .ToList();
            categories.Add(new { name = category.Groups["name"].Value, items });
        }

        WriteJson(Path.Combine(outputRoot, "shortcuts.json"), new { categories });
        Console.WriteLine($"Shortcuts: {categories.Count} categories");
    }

    private static void CopyPowerPlans(string legacy, string outputRoot)
    {
        // Resource names must be derivable from the plan id: framesyncboost -> framesync.
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["framesyncboost"] = "framesync"
        };

        foreach (var existing in Directory.GetFiles(outputRoot, "*.pow"))
        {
            File.Delete(existing);
        }

        foreach (var file in Directory.GetFiles(Path.Combine(legacy, "power-plans"), "*.pow"))
        {
            var stem = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var id = aliases.TryGetValue(stem, out var alias) ? alias : stem;
            File.Copy(file, Path.Combine(outputRoot, id + ".pow"), overwrite: true);
        }
    }

    private static void CopyHelperScripts(string scriptsRoot, string destinationRoot)
    {
        foreach (var file in Directory.GetFiles(scriptsRoot, "*.ps1", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(scriptsRoot, file);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
}
