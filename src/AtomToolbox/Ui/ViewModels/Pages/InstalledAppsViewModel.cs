using System.Collections.ObjectModel;
using System.Windows.Input;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class InstalledTabViewModel : ObservableObject
{
    private bool _isActive;

    public InstalledTabViewModel(string id, string label, Action<string> select)
    {
        Id = id;
        Label = label;
        Command = new RelayCommand(() => select(Id));
    }

    public string Id { get; }
    public string Label { get => _label; set => Set(ref _label, value); }
    public ICommand Command { get; }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    private string _label = "";
}

public sealed class InstalledAppRowViewModel : ObservableObject
{
    private readonly Func<InstalledApp, Task<ActionOutcome>> _uninstall;
    private string _actionText = "UNINSTALL";
    private bool _isBusy;

    public InstalledAppRowViewModel(InstalledApp app, Func<InstalledApp, Task<ActionOutcome>> uninstall)
    {
        App = app;
        _uninstall = uninstall;
        UninstallCommand = new AsyncRelayCommand(RunAsync);
    }

    public InstalledApp App { get; }
    public ICommand UninstallCommand { get; }
    public string Name => App.Name;
    public string Meta => BuildMeta();
    public string ActionText { get => _actionText; private set => Set(ref _actionText, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private string BuildMeta()
    {
        var parts = new List<string>();
        if (App.Publisher.Length > 0) parts.Add(App.Publisher);
        if (App.Version.Length > 0) parts.Add(App.Version);
        if (App.SizeKb > 0) parts.Add($"{App.SizeKb / 1024d:0.0} MB");
        if (App.IsWindowsApp) parts.Add("[WINDOWS APP]");
        return string.Join(" | ", parts);
    }

    private async Task RunAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ActionText = "UNINSTALLING...";
        try
        {
            var result = await _uninstall(App);
            ActionText = result.Ok ? "UNINSTALLED" : "FAILED";
            await Task.Delay(2500);
        }
        finally
        {
            IsBusy = false;
            ActionText = "UNINSTALL";
        }
    }
}

public sealed class InstalledAppsViewModel : PageViewModel
{
    private const int PageSize = 50;
    private static readonly string[] BloatKeywords =
    [
        "microsoft", "windows", "office", "edge", "onedrive", "skype", "teams", "xbox",
        "cortana", "visual studio", ".net", "directx", "adobe"
    ];

    private readonly IInstalledAppService _apps;
    private readonly Action<string, string?> _toast;
    private List<InstalledApp> _all = [];
    private List<InstalledApp> _filtered = [];
    private int _offset;
    private string _selectedTab = "all";
    private string _filterText = "";

    public InstalledAppsViewModel(IInstalledAppService apps, Action<string, string?> toast)
    {
        _apps = apps;
        _toast = toast;
        Title = "INSTALLED APPLICATIONS";
        CountText = "LOADING...";
        HasConsole = false;

        Tabs =
        [
            new InstalledTabViewModel("all", "ALL", SelectTab),
            new InstalledTabViewModel("ms", "MS BLOAT", SelectTab),
            new InstalledTabViewModel("user", "USER", SelectTab)
        ];
        LoadMoreCommand = new RelayCommand(LoadMore);
    }

    public ObservableCollection<InstalledTabViewModel> Tabs { get; }
    public ObservableCollection<InstalledAppRowViewModel> Rows { get; } = new();
    public ICommand LoadMoreCommand { get; }
    public string ShowingText { get; private set; } = "0";
    public bool HasMore { get; private set; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (Set(ref _filterText, value))
            {
                _offset = 0;
                Render();
            }
        }
    }

    public override async Task LoadAsync()
    {
        IsBusy = true;
        BusyText = "FETCHING INSTALLED PACKAGES...";
        try
        {
            _all = (await _apps.ListAsync()).ToList();
            RebuildCounts();
            Render();
        }
        catch (Exception ex)
        {
            _toast("INSTALLED APPS FAILED", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    private void RebuildCounts()
    {
        var ms = _all.Count(a => IsBloat(a));
        Tabs[0].Label = $"ALL ({_all.Count})";
        Tabs[1].Label = $"MS BLOAT ({ms})";
        Tabs[2].Label = $"USER ({_all.Count - ms})";
        CountText = $"{_all.Count} TOTAL";
        foreach (var tab in Tabs)
        {
            tab.IsActive = tab.Id == _selectedTab;
        }
    }

    private static bool IsBloat(InstalledApp app)
    {
        var text = (app.Name + " " + app.Publisher).ToLowerInvariant();
        return BloatKeywords.Any(text.Contains);
    }

    private void SelectTab(string tab)
    {
        _selectedTab = tab;
        _offset = 0;
        foreach (var item in Tabs)
        {
            item.IsActive = item.Id == tab;
        }
        Render();
    }

    private void Render()
    {
        var source = _selectedTab switch
        {
            "ms" => _all.Where(IsBloat),
            "user" => _all.Where(a => !IsBloat(a)),
            _ => _all
        };

        var filter = FilterText.Trim();
        _filtered = (filter.Length == 0
                ? source
                : source.Where(a => a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Rows.Clear();
        _offset = 0;
        Append();
    }

    private void Append()
    {
        var end = Math.Min(_offset + PageSize, _filtered.Count);
        for (var i = _offset; i < end; i++)
        {
            Rows.Add(new InstalledAppRowViewModel(_filtered[i], UninstallAsync));
        }
        _offset = end;
        HasMore = _offset < _filtered.Count;
        ShowingText = $"{Math.Min(_offset, _filtered.Count)} / {_filtered.Count}";
        Raise(nameof(ShowingText));
        Raise(nameof(HasMore));
    }

    private void LoadMore() => Append();

    private async Task<ActionOutcome> UninstallAsync(InstalledApp app)
    {
        if (System.Windows.MessageBox.Show(
                $"Uninstall {app.Name}?",
                "Atom ToolBox",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
        {
            return new ActionOutcome(true, "CANCELLED");
        }

        var result = await _apps.UninstallAsync(app);
        if (result.Ok)
        {
            _toast("APPLICATION UNINSTALLED", app.Name);
            _all.RemoveAll(a => ReferenceEquals(a, app));
            RebuildCounts();
            Render();
        }
        else
        {
            _toast("UNINSTALL FAILED", result.Error);
        }
        return new ActionOutcome(result.Ok, result.Ok ? "UNINSTALLED" : "FAILED");
    }
}
