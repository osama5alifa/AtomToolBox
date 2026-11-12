using System.Collections.ObjectModel;
using System.Windows.Input;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Domain.Tweaks;

namespace Atom.Toolbox.Desktop.ViewModels;

public sealed class NavItemViewModel : ObservableObject
{
    private bool _isActive;

    public required string Id { get; init; }
    public required string Label { get; init; }
    public bool IsLocked { get; init; }
    public bool IsCallToAction { get; init; }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }
}

public sealed class NavGroupViewModel
{
    public required string Title { get; init; }
    public required IReadOnlyList<NavItemViewModel> Items { get; init; }
    public bool HasTitle => Title.Length > 0;
}

public sealed class SearchResultViewModel
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Tag { get; init; }
    public required string PageId { get; init; }
}

public sealed class ShellViewModel : ObservableObject
{
    private readonly ITweakCatalog _catalog;
    private readonly ISystemProbe _probe;
    private readonly ISettingsStore _settings;
    private readonly IIntegrityService _integrity;
    private readonly PageFactory _pages;
    private readonly Func<AppTheme, Task> _themeApplier;

    private PageViewModel? _currentPage;
    private string _currentTitle = "";
    private string _searchText = "";
    private bool _isSearchOpen;
    private string _cpuText = "CPU: --";
    private string _gpuText = "GPU: --";
    private string _osText = "OS: --";
    private string _integrityText = "SYSTEM_SECURE";
    private string _profileText = "Windows_NT";
    private string _sessionText = "00:00:00";
    private string _toastMessage = "";
    private string _toastSub = "";
    private bool _isToastVisible;
    private CancellationTokenSource? _toastSource;
    private CancellationTokenSource? _searchSource;
    private bool _initialized;

    public ShellViewModel(
        ITweakCatalog catalog,
        ISystemProbe probe,
        ISettingsStore settings,
        IIntegrityService integrity,
        PageFactory pages,
        Func<AppTheme, Task> themeApplier)
    {
        _catalog = catalog;
        _probe = probe;
        _settings = settings;
        _integrity = integrity;
        _pages = pages;
        _themeApplier = themeApplier;

        NavigateCommand = new RelayCommand(parameter => Navigate((parameter as NavItemViewModel)?.Id));
        OpenSearchResultCommand = new RelayCommand(parameter =>
        {
            if (parameter is SearchResultViewModel result)
            {
                SearchText = "";
                Navigate(result.PageId);
            }
        });
        MinimizeCommand = new RelayCommand(() => WindowStateCommand?.Invoke("minimize"));
        MaximizeCommand = new RelayCommand(() => WindowStateCommand?.Invoke("maximize"));
        CloseCommand = new RelayCommand(() => WindowStateCommand?.Invoke("close"));
        DismissToastCommand = new RelayCommand(() => IsToastVisible = false);

        NavGroups = BuildNavigation();
    }

    public IReadOnlyList<NavGroupViewModel> NavGroups { get; }
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = new();
    public ICommand NavigateCommand { get; }
    public ICommand OpenSearchResultCommand { get; }
    public ICommand MinimizeCommand { get; }
    public ICommand MaximizeCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand DismissToastCommand { get; }

    /// <summary>Raised for minimize/maximize/close and handled by the window chrome.</summary>
    public static event Action<string>? WindowStateCommand;

    public PageViewModel? CurrentPage
    {
        get => _currentPage;
        private set => Set(ref _currentPage, value);
    }

    public string CurrentTitle
    {
        get => _currentTitle;
        private set => Set(ref _currentTitle, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value))
            {
                return;
            }
            ScheduleSearch(value);
        }
    }

    public bool IsSearchOpen
    {
        get => _isSearchOpen;
        set => Set(ref _isSearchOpen, value);
    }

    public bool HasSearchResults => SearchResults.Count > 0;

    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }
    public string GpuText { get => _gpuText; private set => Set(ref _gpuText, value); }
    public string OsText { get => _osText; private set => Set(ref _osText, value); }
    public string IntegrityText { get => _integrityText; private set => Set(ref _integrityText, value); }
    public string ProfileText { get => _profileText; private set => Set(ref _profileText, value); }
    public string SessionText { get => _sessionText; private set => Set(ref _sessionText, value); }

    public string ToastMessage { get => _toastMessage; private set => Set(ref _toastMessage, value); }
    public string ToastSub { get => _toastSub; private set => Set(ref _toastSub, value); }
    public bool IsToastVisible { get => _isToastVisible; private set => Set(ref _isToastVisible, value); }
    public bool ShowToastSub => ToastSub.Length > 0;

    public string VersionText => "Atom ToolBox v0.2.5";
    public string LogoPath => _settings.Current.Theme == AppTheme.Light
        ? "pack://application:,,,/Assets/Logos/logo-light.png"
        : "pack://application:,,,/Assets/Logos/logo-dark.png";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        var startPage = Environment.GetEnvironmentVariable("ATOM_START_PAGE");
        Navigate(string.IsNullOrWhiteSpace(startPage) ? "dashboard" : startPage);
        ModestUpdate();
        await RefreshHeaderAsync().ConfigureAwait(true);

        var integrity = await _integrity.VerifyAsync().ConfigureAwait(true);
        IntegrityText = integrity.Ok ? "SYSTEM_SECURE" : "INTEGRITY_WARN";
    }

    public async Task RefreshHeaderAsync()
    {
        try
        {
            var snapshot = await _probe.CaptureAsync().ConfigureAwait(true);
            ModestUpdate(snapshot);
        }
        catch (Exception ex)
        {
            AppLog.Error("header", ex);
        }
    }

    private void ModestUpdate(Domain.System.SystemSnapshot? snapshot = null)
    {
        if (snapshot is not null)
        {
            CpuText = $"CPU: {snapshot.CpuPercent}%";
            GpuText = $"GPU: {snapshot.GpuName}";
            OsText = $"OS: {snapshot.OsBuild}";
            SessionText = FormatUptime(snapshot.Uptime);
        }

        ProfileText = _settings.ActiveProfile;
    }

    public void Navigate(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return;
        }

        _pages.SetShell(this);
        CurrentPage?.Deactivate();
        var page = _pages.Create(pageId);
        if (page is null)
        {
            return;
        }

        CurrentPage = page;
        CurrentTitle = page.Title;
        _ = LoadPageAsync(page);
        UpdateActiveNav(pageId);    }

    private async Task LoadPageAsync(PageViewModel page)
    {
        try
        {
            await page.LoadAsync().ConfigureAwait(true);
            if (!ReferenceEquals(CurrentPage, page))
            {
                return;
            }

            await RefreshHeaderAsync().ConfigureAwait(true);
            PageReady?.Invoke();
        }
        catch (Exception ex)
        {
            ShowToast("PAGE FAILED TO LOAD", ex.Message);
            AppLog.Error("page-load", ex);
        }
    }

    private void UpdateActiveNav(string pageId)
    {
        foreach (var group in NavGroups)
        {
            foreach (var item in group.Items)
            {
                item.IsActive = item.Id == pageId;
            }
        }
    }

    public event Action? PageReady;

    private void ScheduleSearch(string text)
    {
        _searchSource?.Cancel();
        _searchSource?.Dispose();

        if (string.IsNullOrWhiteSpace(text))
        {
            SearchResults.Clear();
            IsSearchOpen = false;
            Raise(nameof(HasSearchResults));
            return;
        }

        var source = new CancellationTokenSource();
        _searchSource = source;
        var token = source.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(80, token).ConfigureAwait(false);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        RebuildSearch(text);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // A newer keystroke replaced this search.
            }
        });
    }

    private void RebuildSearch(string text)
    {
        SearchResults.Clear();
        var query = text.Trim();
        if (query.Length == 0)
        {
            IsSearchOpen = false;
            Raise(nameof(HasSearchResults));
            return;
        }

        foreach (var (page, tweak) in _catalog.Search(query).Take(12))
        {
            SearchResults.Add(new SearchResultViewModel
            {
                Title = tweak.Name,
                Subtitle = tweak.Description,
                Tag = page.Tag,
                PageId = page.Id
            });
        }

        IsSearchOpen = SearchResults.Count > 0;
        Raise(nameof(HasSearchResults));
    }

    public void ShowToast(string message, string? sub = null)
    {
        ToastMessage = message;
        ToastSub = sub ?? "";
        Raise(nameof(ShowToastSub));
        IsToastVisible = true;
        _toastSource?.Cancel();
        _toastSource?.Dispose();
        _toastSource = new CancellationTokenSource();
        var token = _toastSource.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2800, token).ConfigureAwait(false);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => IsToastVisible = false);
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer toast.
            }
        });
    }

    public async Task ApplyThemeAsync(AppTheme theme)
    {
        await _themeApplier(theme).ConfigureAwait(true);
        Raise(nameof(LogoPath));
    }

    public void RefreshThemeDependentState() => Raise(nameof(LogoPath));

    private IReadOnlyList<NavGroupViewModel> BuildNavigation()
    {
        return
        [
            Group("SYSTEM", "DASHBOARD", "BACKGROUND MONITOR", "SHORTCUTS"),
            Group("OPTIMIZATIONS", "WINDOWS", "GAMING", "GPU", "POWER", "NETWORK", "PRIVACY SETTINGS"),
            Group("TOOLS & UTILITIES", "CONTEXT MENU", "BRAVE DEBLOATER", "WINDOWS DEBLOAT", "CLEANER", "POWER PLANS", "STARTUP MANAGER", "PERSONALIZE"),
            Advanced(),
            Group("MANAGEMENT & SAFETY", "BACKUP & RESTORE", "APP INSTALLER", "INSTALLED APPS", "HISTORY", "SETTINGS")
        ];
    }

    private static NavGroupViewModel Group(string title, params string[] labels) => new()
    {
        Title = title,
        Items = labels.Select(label => new NavItemViewModel { Id = IdFor(label), Label = label }).ToArray()
    };

    private static NavGroupViewModel Advanced() => new()
    {
        Title = "ADVANCED",
        Items =
        [
            new NavItemViewModel { Id = "advpower", Label = "ADVANCED POWER", IsLocked = true },
            new NavItemViewModel { Id = "winupdate", Label = "WINDOWS UPDATE", IsLocked = true },
            new NavItemViewModel { Id = "dns", Label = "DNS OPTIMIZER", IsLocked = true },
            new NavItemViewModel { Id = "drivers", Label = "DRIVER HUB", IsLocked = true },
        ]
    };

    public static string IdFor(string label) => label switch
    {
        "DASHBOARD" => "dashboard",
        "BACKGROUND MONITOR" => "monitor",
        "SHORTCUTS" => "shortcuts",
        "WINDOWS" => "windows",
        "GAMING" => "gaming",
        "GPU" => "gpu",
        "POWER" => "power",
        "NETWORK" => "network",
        "PRIVACY SETTINGS" => "privacy",
        "CONTEXT MENU" => "contextmenu",
        "BRAVE DEBLOATER" => "brave",
        "WINDOWS DEBLOAT" => "debloat",
        "CLEANER" => "cleaner",
        "POWER PLANS" => "powerplans",
        "STARTUP MANAGER" => "startup",
        "PERSONALIZE" => "personalize",
        "BACKUP & RESTORE" => "backup",
        "APP INSTALLER" => "apps",
        "INSTALLED APPS" => "applist",
        "HISTORY" => "history",
        "SETTINGS" => "settings",
        _ => label.ToLowerInvariant()
    };

    private static string FormatUptime(TimeSpan uptime) =>
        $"{(int)uptime.TotalHours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
}
