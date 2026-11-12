using System.Collections.ObjectModel;
using System.Windows.Input;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels.Items;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class DebloatRowViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public DebloatRowViewModel(DebloatApp app, Action selectionChanged)
    {
        App = app;
        _selectionChanged = selectionChanged;
    }

    public DebloatApp App { get; }
    public string Id => App.Id;
    public string Name => App.Name;
    public string Description => App.Description;
    public string Packages => string.Join(" | ", App.PackageNames);
    public bool CanRemove => App.CanRemove;
    public bool IsAbsent => App.Absent;
    public string Meta => BuildMeta();
    public string Badge => !App.CanRemove && App.Installed
        ? "PROTECTED"
        : App.Provisioned && !App.Installed ? "PROVISIONED ONLY" : "";
    public bool HasBadge => Badge.Length > 0;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value && CanRemove))
            {
                _selectionChanged();
            }
        }
    }

    private string BuildMeta()
    {
        var bits = new List<string>();
        if (App.InstalledCount > 0) bits.Add($"INSTALLED {App.InstalledCount}");
        if (App.ProvisionedCount > 0) bits.Add($"PROVISIONED {App.ProvisionedCount}");
        if (App.Absent) bits.Add("NOT INSTALLED");
        if (App.ProtectedCount > 0) bits.Add($"{App.ProtectedCount} PROTECTED");
        return string.Join(" | ", bits);
    }
}

public sealed class DebloatGroupViewModel
{
    public required string Category { get; init; }
    public required int RemovableCount { get; init; }
    public required IReadOnlyList<DebloatRowViewModel> Rows { get; init; }
}

public sealed class DebloatViewModel : PageViewModel
{
    private readonly IDebloatService _debloat;
    private readonly IEnvironmentService _environment;
    private readonly Action<string, string?> _toast;
    private IReadOnlyList<DebloatApp> _apps = [];
    private bool _showAbsent;
    private bool _removing;
    private string _statusText = "READY.";
    private StatusTone _statusTone = StatusTone.Neutral;
    private string _filterText = "";
    private int _selectedCount;
    private int _removableTotal;

    public DebloatViewModel(IDebloatService debloat, IEnvironmentService environment, Action<string, string?> toast)
    {
        _debloat = debloat;
        _environment = environment;
        _toast = toast;
        Title = "WINDOWS APP DEBLOAT";
        CountText = "SCANNING...";
        ShowAdminWarning = !environment.IsElevated;
        Intro = "SELECT PREINSTALLED APPS AND REMOVE THEM FOR ALL USERS. PROVISIONED COPIES ARE ALSO REMOVED SO WINDOWS DOES NOT REINSTALL THEM FOR NEW USERS. REMOVAL CANNOT BE UNDONE FROM THIS APP.";
        HasConsole = false;

        SelectAllCommand = new RelayCommand(SelectAllVisible);
        ClearCommand = new RelayCommand(ClearSelection);
        ToggleAbsentCommand = new RelayCommand(ToggleAbsent);
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(force: true));
        RemoveCommand = new AsyncRelayCommand(RemoveAsync);

        Toolbar.Add(new ActionButtonViewModel("SELECT ALL VISIBLE", SelectAllCommand));
        Toolbar.Add(new ActionButtonViewModel("CLEAR", ClearCommand));
        Toolbar.Add(new ActionButtonViewModel("SHOW NOT INSTALLED", ToggleAbsentCommand));
        Toolbar.Add(new ActionButtonViewModel("REFRESH", RefreshCommand));
        Toolbar.Add(new ActionButtonViewModel("REMOVE SELECTED", RemoveCommand));
    }

    public ObservableCollection<DebloatGroupViewModel> Groups { get; } = new();
    public ICommand SelectAllCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ToggleAbsentCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RemoveCommand { get; }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public StatusTone StatusTone { get => _statusTone; private set => Set(ref _statusTone, value); }
    public int SelectedCount { get => _selectedCount; private set { if (Set(ref _selectedCount, value)) Raise(nameof(RemoveLabel)); } }
    public string RemoveLabel => $"REMOVE SELECTED ({SelectedCount})";
    public string EmptyMessage => "NO CATALOGED WINDOWS APPS WERE FOUND ON THIS PC.";
    public bool CanRemoveSelected => SelectedCount > 0 && !_removing && _environment.IsElevated;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (Set(ref _filterText, value))
            {
                RebuildGroups();
            }
        }
    }

    public override async Task LoadAsync() => await LoadAsync(force: false);

    private async Task LoadAsync(bool force)
    {
        IsBusy = true;
        BusyText = "SCANNING INSTALLED WINDOWS APPS...";
        try
        {
            var result = await _debloat.ScanAsync(force);
            _apps = result.Apps;
            _removableTotal = _apps.Count(a => a.CanRemove);
            CountText = $"{_removableTotal} REMOVABLE / {_apps.Count} CATALOG";
            RebuildGroups();

            var scopeNote = result.Partial ? " [SCAN LIMITED]" : "";
            if (!_environment.IsElevated)
            {
                StatusTone = StatusTone.Failed;
                StatusText = "ADMINISTRATOR TOKEN NOT DETECTED - RESTART THE APP AS ADMINISTRATOR BEFORE REMOVING APPS.";
            }
            else
            {
                StatusTone = StatusTone.On;
                StatusText = $"SCAN COMPLETE - {_removableTotal} app group(s) can be removed on this PC. Removal applies to all users.{scopeNote}";
            }
        }
        catch (Exception ex)
        {
            StatusTone = StatusTone.Failed;
            StatusText = "SCAN FAILED - " + ex.Message;
            _toast("DEBLOAT SCAN FAILED", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        var filter = FilterText.Trim();
        var visible = _apps
            .Where(app => _showAbsent || !app.Absent)
            .Where(app => filter.Length == 0 ||
                          app.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                          app.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                          app.Description.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                          app.PackageNames.Any(p => p.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var category in visible.Select(a => a.Category).Distinct())
        {
            var apps = visible.Where(a => a.Category == category).ToList();
            Groups.Add(new DebloatGroupViewModel
            {
                Category = category,
                RemovableCount = apps.Count(a => a.CanRemove),
                Rows = apps.Select(a => new DebloatRowViewModel(a, UpdateSelection)).ToArray()
            });
        }

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        SelectedCount = Groups.SelectMany(g => g.Rows).Count(r => r.IsSelected);
        Raise(nameof(CanRemoveSelected));
        Raise(nameof(RemoveLabel));
    }

    private void SelectAllVisible()
    {
        foreach (var row in Groups.SelectMany(g => g.Rows).Where(r => r.CanRemove))
        {
            row.IsSelected = true;
        }
    }

    private void ClearSelection()
    {
        foreach (var row in Groups.SelectMany(g => g.Rows))
        {
            row.IsSelected = false;
        }
    }

    private void ToggleAbsent()
    {
        _showAbsent = !_showAbsent;
        Toolbar[2] = new ActionButtonViewModel(
            _showAbsent ? "HIDE NOT INSTALLED" : "SHOW NOT INSTALLED",
            ToggleAbsentCommand);
        RebuildGroups();
    }

    private async Task RemoveAsync()
    {
        if (_removing || SelectedCount == 0)
        {
            return;
        }

        var ids = Groups.SelectMany(g => g.Rows).Where(r => r.IsSelected).Select(r => r.Id).ToArray();
        if (System.Windows.MessageBox.Show(
                $"Remove {ids.Length} selected app group(s) for ALL users?\n\nProvisioned copies will also be removed. This cannot be undone from AtomToolBox.",
                "Atom ToolBox",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        _removing = true;
        IsBusy = true;
        BusyText = "REMOVING SELECTED APPS... THIS CAN TAKE SEVERAL MINUTES.";
        StatusTone = StatusTone.Working;
        StatusText = "REMOVING SELECTED APPS... THIS CAN TAKE SEVERAL MINUTES. DO NOT CLOSE THE APP.";
        Raise(nameof(CanRemoveSelected));
        try
        {
            var result = await _debloat.RemoveAsync(ids, new Progress<string>(line => BusyText = line));
            var bad = result.Results.Count(r => !r.Ok);
            var good = result.Results.Count - bad;
            StatusTone = bad == 0 ? StatusTone.On : StatusTone.Failed;
            StatusText = bad == 0
                ? $"REMOVAL COMPLETE - {good} app group(s) removed for all users."
                : $"FINISHED WITH ISSUES - {good} clean, {bad} with errors.";
            _toast(bad == 0 ? "DEBLOAT COMPLETE" : "DEBLOAT FINISHED WITH ISSUES", $"{good} clean / {bad} issues");
            await LoadAsync(force: true);
        }
        catch (Exception ex)
        {
            StatusTone = StatusTone.Failed;
            StatusText = "REMOVAL FAILED - " + ex.Message;
            _toast("REMOVAL FAILED", ex.Message);
        }
        finally
        {
            _removing = false;
            IsBusy = false;
            BusyText = "";
            Raise(nameof(CanRemoveSelected));
        }
    }
}
