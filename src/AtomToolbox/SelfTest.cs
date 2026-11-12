using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Infrastructure.Catalog;
using Atom.Toolbox.Infrastructure.ContextMenu;
using Atom.Toolbox.Infrastructure.Power;
using Atom.Toolbox.Infrastructure.Processes;
using Atom.Toolbox.Infrastructure.Registry;
using Atom.Toolbox.Infrastructure.Startup;
using Atom.Toolbox.Infrastructure.SystemInfo;
using Atom.Toolbox.Infrastructure.Tweaks;

namespace Atom.Toolbox.Desktop;

/// <summary>
/// Headless engine diagnostic used by the release checklist:
/// <c>AtomToolbox.exe --self-test</c>. It exercises a real, reversible HKCU tweak,
/// the powercfg reader, the startup scan and the context-menu state reader.
/// </summary>
internal static class SelfTest
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string TintValue = "ColorPrevalence";

    public static async Task<int> RunAsync()
    {
        var report = new List<string>
        {
            "ATOM TOOLBOX — ENGINE SELF-TEST",
            $"Started: {DateTimeOffset.Now:O}",
            ""
        };
        var failures = 0;

        void Check(string name, bool ok, string? detail = null)
        {
            report.Add($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail is null ? "" : " — " + detail)}");
            if (!ok)
            {
                failures++;
            }
        }

        try
        {
            var environment = new EnvironmentService();
            var registry = new RegistryStore();
            var processes = ProcessRunner.Shared;
            var catalog = CatalogResources.Tweaks;
            var powerPlans = new PowerPlanService(processes, new PowerPlanCatalog(), environment, registry);
            var executor = new TweakExecutor(
                registry,
                new ServiceHost(),
                processes,
                environment,
                powerPlans,
                new AccentService(registry, environment));

            Check("catalog", catalog.TotalTweakCount == 234, $"{catalog.TotalTweakCount} tweaks / {catalog.Pages.Count} pages");
            Check("free pages", catalog.FreePages.Count == 8, $"{catalog.FreeTweakCount} free tweaks");
            Check("bundled power plans", CatalogResources.Plans.Count == 20, $"{CatalogResources.Plans.Count} plans");

            var shortcutCategories = CatalogResources.ShortcutCategories;
            var shortcutCount = shortcutCategories.Sum(c => c.Items.Count);
            Check("shortcut catalog", shortcutCategories.Count > 0 && shortcutCount > 0,
                $"{shortcutCount} shortcuts / {shortcutCategories.Count} categories");

            var page = catalog.GetPage("personalize");
            var tweak = page.Tweaks.First(t => t.Id == "tb_tint");
            var hadOriginal = registry.TryRead("HKCU", PersonalizeKey, TintValue, out var original);
            var registryWritable = true;
            try
            {
                var applied = await executor.ApplyAsync(tweak, true);
                if (!applied.Ok && applied.Error?.Contains("denied", StringComparison.OrdinalIgnoreCase) == true)
                {
                    registryWritable = false;
                    report.Add("[SKIP] registry write — this environment blocks HKCU writes.");
                }
                else
                {
                    Check("apply registry tweak", applied.Ok, applied.Error);
                    if (applied.Ok)
                    {
                        var on = await executor.VerifyAsync(tweak);
                        Check("verify tweak ON", on.Enabled && on.Error is null, on.Error);

                        var reverted = await executor.ApplyAsync(tweak, false);
                        Check("revert registry tweak", reverted.Ok, reverted.Error);
                        var off = await executor.VerifyAsync(tweak);
                        Check("verify tweak OFF", !off.Enabled && off.Error is null, off.Error);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                registryWritable = false;
                report.Add("[SKIP] registry write — this environment blocks HKCU writes.");
            }
            finally
            {
                try
                {
                    if (hadOriginal && original.Data is not null)
                    {
                        registry.SetDWord("HKCU", PersonalizeKey, TintValue,
                            Convert.ToInt64(original.Data, System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        registry.DeleteValue("HKCU", PersonalizeKey, TintValue);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // The sandbox blocked the test write; there is nothing to restore.
                }
            }

            if (!registryWritable)
            {
                report.Add("       (registry write checks skipped; run this command elevated on a normal desktop.)");
            }

            var plans = await powerPlans.ListAsync();
            Check("powercfg list", plans.Count >= 3, $"{plans.Count} plans");
            var active = plans.FirstOrDefault(p => p.IsActive) ?? plans.First();
            var scheme = await powerPlans.ReadSchemeAsync(active.Guid);
            Check("powercfg read scheme", scheme.Ok && scheme.Value is { SettingsCount: > 0 },
                scheme.Ok ? $"{scheme.Value?.SettingsCount} settings for {active.Name}" : scheme.Error);

            var startup = new StartupService(registry);
            var scan = await startup.ScanAsync();
            Check("startup scan", scan.Entries.Count > 0, $"{scan.Entries.Count} entries");

            var menu = new ContextMenuService(registry);
            var menuState = await menu.GetStateAsync();
            Check("context menu state", menuState.Count == menu.Entries.Count, $"{menuState.Count} entries");
        }
        catch (Exception ex)
        {
            failures++;
            report.Add("[FAIL] unhandled: " + ex);
        }

        report.Add("");
        report.Add(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures} check(s))");
        var output = Path.Combine(Path.GetTempPath(), "atom-selftest.txt");
        try
        {
            await File.WriteAllLinesAsync(output, report);
        }
        catch (Exception)
        {
            // Console output below is the primary channel.
        }

        foreach (var line in report)
        {
            Console.WriteLine(line);
        }
        return failures == 0 ? 0 : 1;
    }
}
