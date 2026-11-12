using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels.Items;
using Atom.Toolbox.Domain.Tweaks;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class TweakPageViewModel : PageViewModel
{
    private readonly TweakPage _page;
    private readonly ITweakService _tweaks;
    private readonly IEnvironmentService _environment;
    private readonly Action<string, string?> _toast;
    private readonly Dictionary<string, ToggleRowViewModel> _rows = new(StringComparer.OrdinalIgnoreCase);

    public TweakPageViewModel(
        TweakPage page,
        ITweakService tweaks,
        IEnvironmentService environment,
        Action<string, string?> toast)
    {
        _page = page;
        _tweaks = tweaks;
        _environment = environment;
        _toast = toast;

        Title = page.Title;
        CountText = $"({page.Tweaks.Count} TWEAKS)";
        ShowAdminWarning = !environment.IsElevated;
        ShowPreview = true;
        PreviewTitle = "LIVE BATCH SCRIPT PREVIEW";
        Toolbar.Add(new ActionButtonViewModel("CHECK STATE", new AsyncRelayCommand(CheckStateAsync)));
    }

    public override Task LoadAsync()
    {
        Sections.Clear();
        _rows.Clear();

        var rows = new List<object>(_page.Tweaks.Count);
        foreach (var tweak in _page.Tweaks)
        {
            var row = new ToggleRowViewModel(
                tweak.Name,
                tweak.Description,
                _tweaks.IsOn(_page, tweak),
                desired => ToggleAsync(tweak, desired));
            _rows[tweak.Id] = row;
            rows.Add(row);
        }

        Sections.Add(new PageSectionViewModel
        {
            Items = rows,
            Margin = new System.Windows.Thickness(0)
        });

        UpdatePreview();
        Console.Reset();
        return Task.CompletedTask;
    }

    private async Task<ToggleOutcome> ToggleAsync(TweakDefinition tweak, bool desired)
    {
        var result = await _tweaks.ToggleAsync(_page, tweak, desired, new Progress<string>(Console.Write));
        UpdatePreview();
        if (!result.Ok)
        {
            Console.Write($"ERROR: {result.Error}");
            return ToggleOutcome.Fail(
                result.Error?.Contains("administrator", StringComparison.OrdinalIgnoreCase) == true,
                result.Error);
        }

        _toast(desired ? "TWEAK APPLIED" : "TWEAK REVERTED", tweak.Name);
        return ToggleOutcome.Success;
    }

    private async Task CheckStateAsync()
    {
        IsBusy = true;
        BusyText = "SCANNING SYSTEM STATE...";
        Console.Write($"Checking {_page.Tweaks.Count} tweaks...");
        try
        {
            var active = await _tweaks.VerifyPageAsync(
                _page,
                new Progress<int>(done => BusyText = $"SCANNING... {done}/{_page.Tweaks.Count}"));

            foreach (var tweak in _page.Tweaks)
            {
                if (_rows.TryGetValue(tweak.Id, out var row))
                {
                    row.IsOn = _tweaks.IsOn(_page, tweak);
                }
            }

            UpdatePreview();
            Console.Write($"State check complete: {active} of {_page.Tweaks.Count} tweaks verified active.");
            _toast("REGISTRY STATE SYNCHRONIZED", $"{active} OF {_page.Tweaks.Count} TWEAKS VERIFIED ACTIVE");
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    private void UpdatePreview()
    {
        var lines = new List<string>();
        foreach (var tweak in _page.Tweaks)
        {
            if (!_tweaks.IsOn(_page, tweak))
            {
                continue;
            }

            foreach (var action in tweak.Apply)
            {
                lines.Add(Describe(action));
            }
        }

        PreviewText = lines.Count > 0 ? string.Join('\n', lines) : "-- NO ACTIVE TWEAKS --";
    }

    private static string Describe(TweakAction action) => action.Kind switch
    {
        TweakActionKinds.Registry => $"reg add \"{action.Hive}\\{action.Path}\" /v \"{action.Name}\" /t {action.Type} /d {action.Value} /f",
        TweakActionKinds.RegistryDelete => $"reg delete \"{action.Hive}\\{action.Path}\"" +
                                           (string.IsNullOrEmpty(action.Name) ? " /f" : $" /v \"{action.Name}\" /f"),
        TweakActionKinds.Service => $"sc config {action.Service} start= {action.Startup ?? action.Control}",
        TweakActionKinds.PowerCfg => "powercfg " + string.Join(' ', action.Arguments ?? Array.Empty<string>()),
        TweakActionKinds.PowerScheme => "powercfg /duplicatescheme " + string.Join(' ', action.Arguments ?? Array.Empty<string>()),
        TweakActionKinds.Accent => $"accent #{action.Color}",
        TweakActionKinds.PowerShell => "powershell " + Truncate(action.Script),
        TweakActionKinds.Process => action.Executable + " " + string.Join(' ', action.Arguments ?? Array.Empty<string>()),
        TweakActionKinds.Batch => "-- legacy helper script --",
        _ => action.Kind
    };

    private static string Truncate(string? script) =>
        string.IsNullOrEmpty(script) ? "" : script.Length <= 120 ? script : script[..120] + "...";
}
