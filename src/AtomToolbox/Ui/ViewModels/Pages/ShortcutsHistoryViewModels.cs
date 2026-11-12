using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels.Items;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class ShortcutsViewModel : PageViewModel
{
    private readonly IEnvironmentService _environment;
    private readonly Action<string, string?> _toast;

    public ShortcutsViewModel(IShortcutCatalog catalog, IEnvironmentService environment, Action<string, string?> toast)
    {
        _environment = environment;
        _toast = toast;
        Title = "WINDOWS SHORTCUTS";
        CountText = $"({catalog.Categories.Sum(c => c.Items.Count)} SHORTCUTS)";
        Intro = "CLICK ANY BUTTON TO OPEN THE CORRESPONDING WINDOWS SETTING OR TOOL INSTANTLY.";
        HasConsole = false;

        foreach (var category in catalog.Categories)
        {
            Sections.Add(new PageSectionViewModel
            {
                Title = category.Name,
                IsGrid = true,
                Items = category.Items.Select(item => (object)new ButtonGridItemViewModel
                {
                    Label = item.Name,
                    Command = new RelayCommand(() => Open(item))
                }).ToArray()
            });
        }
    }

    private void Open(ShortcutItem item)
    {
        try
        {
            _environment.OpenShortcut(item.Command);
            _toast("OPENED", item.Name);
        }
        catch (Exception ex)
        {
            _toast("COULD NOT OPEN", ex.Message);
        }
    }
}

public sealed class HistoryViewModel : PageViewModel
{
    private readonly IHistoryService _history;
    private readonly Action<string, string?> _toast;

    public HistoryViewModel(IHistoryService history, Action<string, string?> toast)
    {
        _history = history;
        _toast = toast;
        Title = "UNDO HISTORY";
        Intro = "CLICK REVERT TO UNDO A SPECIFIC TWEAK. REVERT ALL PROCESSES EVERY ENTRY FROM MOST RECENT TO OLDEST.";
        Toolbar.Add(new ActionButtonViewModel("REVERT ALL", new AsyncRelayCommand(RevertAllAsync)));
        Toolbar.Add(new ActionButtonViewModel("CLEAR HISTORY", new RelayCommand(Clear)));
    }

    public override Task LoadAsync()
    {
        Rebuild();
        return Task.CompletedTask;
    }

    private void Rebuild()
    {
        Sections.Clear();
        var entries = _history.Entries;
        CountText = $"({entries.Count} ENTRIES)";

        if (entries.Count == 0)
        {
            Sections.Add(new PageSectionViewModel
            {
                Items = [new MessageViewModel
                {
                    Text = "NO TWEAKS HAVE BEEN APPLIED YET IN THIS SESSION. TOGGLE ANY TWEAK TO SEE IT HERE."
                }]
            });
            return;
        }

        var rows = new List<object>(entries.Count);
        foreach (var entry in entries)
        {
            rows.Add(new ActionRowViewModel(
                entry.Name,
                $"{entry.Category.ToUpperInvariant()} | {entry.Timestamp.LocalDateTime:g} | {entry.ActionLabel}",
                "REVERT",
                () => RevertAsync(entry)));
        }
        Sections.Add(new PageSectionViewModel { Items = rows, Margin = new System.Windows.Thickness(0) });
    }

    private async Task<ActionOutcome> RevertAsync(HistoryEntry entry)
    {
        var result = await _history.RevertAsync(entry, new Progress<string>(Console.Write));
        if (!result.Ok)
        {
            _toast("REVERT FAILED", result.Error);
            return new ActionOutcome(false, "FAILED");
        }

        Rebuild();
        _toast("REVERTED", entry.Name);
        return new ActionOutcome(true, "REVERTED");
    }

    private async Task RevertAllAsync()
    {
        var entries = _history.Entries.ToArray();
        if (entries.Length == 0)
        {
            return;
        }

        if (System.Windows.MessageBox.Show(
                $"Revert all {entries.Length} history entries?",
                "Atom ToolBox",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        BusyText = "REVERTING HISTORY...";
        try
        {
            foreach (var entry in entries)
            {
                var result = await _history.RevertAsync(entry, new Progress<string>(Console.Write));
                Console.Write(result.Ok ? $"{entry.Name}: reverted." : $"{entry.Name}: {result.Error}");
            }

            _history.Clear();
            Rebuild();
            _toast("HISTORY REVERTED", $"{entries.Length} ENTRIES PROCESSED");
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    private void Clear()
    {
        _history.Clear();
        Rebuild();
        _toast("HISTORY CLEARED", null);
    }
}
