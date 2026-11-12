using System.Collections.ObjectModel;
using System.Windows.Input;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Domain.Power;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class PowerPlanRowViewModel : ObservableObject
{
    private readonly Func<PowerPlan, Task> _activate;
    private bool _isBusy;
    private string _actionText = "ACTIVATE";

    public PowerPlanRowViewModel(PowerPlan plan, Func<PowerPlan, Task> activate)
    {
        Plan = plan;
        _activate = activate;
        ActivateCommand = new AsyncRelayCommand(ActivateAsync);
    }

    public PowerPlan Plan { get; }
    public string Name => Plan.Name;
    public string Guid => Plan.Guid;
    public bool IsActive => Plan.IsActive;
    public bool IsBuiltIn => Plan.IsBuiltIn;
    public ICommand ActivateCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                Raise(nameof(CanActivate));
            }
        }
    }

    public bool CanActivate => !IsActive && !IsBusy;

    public string ActionText
    {
        get => _actionText;
        private set => Set(ref _actionText, value);
    }

    public override string ToString() => Name;

    private async Task ActivateAsync()
    {
        if (!CanActivate)
        {
            return;
        }

        IsBusy = true;
        ActionText = "ACTIVATING...";
        try
        {
            await _activate(Plan);
        }
        finally
        {
            IsBusy = false;
            ActionText = "ACTIVATE";
        }
    }
}

/// <summary>
/// The bundled plans are imported once and cached by their GUID. Applying one is still just a
/// plan switch: no per-setting editor is exposed here.
/// </summary>
public sealed class BundledPlanRowViewModel : ObservableObject
{
    private readonly Func<BundledPowerPlan, Task> _apply;
    private bool _isBusy;
    private string _actionText = "APPLY";

    public BundledPlanRowViewModel(BundledPowerPlan plan, Func<BundledPowerPlan, Task> apply)
    {
        Plan = plan;
        _apply = apply;
        ApplyCommand = new AsyncRelayCommand(ApplyAsync);
    }

    public BundledPowerPlan Plan { get; }
    public string Name => Plan.Name;
    public string Description => Plan.Description;
    public bool IsActive => false;
    public ICommand ApplyCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                Raise(nameof(CanApply));
            }
        }
    }

    public bool CanApply => !IsBusy;

    public string ActionText
    {
        get => _actionText;
        private set => Set(ref _actionText, value);
    }

    private async Task ApplyAsync()
    {
        if (!CanApply)
        {
            return;
        }

        IsBusy = true;
        ActionText = "APPLYING...";
        try
        {
            await _apply(Plan);
        }
        finally
        {
            IsBusy = false;
            ActionText = "APPLY";
        }
    }
}

/// <summary>
/// Minimal power-plan switcher: installed schemes, the active scheme, and the full set of
/// ready-made plans that shipped with the legacy build. No internal settings or editors.
/// </summary>
public sealed class PowerPlansViewModel : PageViewModel
{
    private readonly IPowerPlanService _power;
    private readonly Action<string, string?> _toast;
    private string _activeName = "—";
    private string _activeGuid = "—";
    private bool _hasPlans;
    private int _planCount;

    public PowerPlansViewModel(
        IPowerPlanService power,
        IPowerPlanCatalog catalog,
        IEnvironmentService environment,
        Action<string, string?> toast)
    {
        _power = power;
        _toast = toast;

        Title = "POWER PLANS";
        Intro = "SELECT A POWER PLAN. WINDOWS APPLIES IT IMMEDIATELY; THE ACTIVE SCHEME IS MARKED. READY-MADE PLANS ARE IMPORTED ONCE AND ACTIVATED.";
        ShowAdminWarning = !environment.IsElevated;
        HasConsole = false;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        Toolbar.Add(new ActionButtonViewModel("REFRESH", RefreshCommand));

        foreach (var plan in catalog.Plans)
        {
            BundledPlans.Add(new BundledPlanRowViewModel(plan, ApplyBundledAsync));
        }
    }

    public ObservableCollection<PowerPlanRowViewModel> Plans { get; } = new();
    public ObservableCollection<BundledPlanRowViewModel> BundledPlans { get; } = new();
    public ICommand RefreshCommand { get; }

    public string ActiveName { get => _activeName; private set => Set(ref _activeName, value); }
    public string ActiveGuid { get => _activeGuid; private set => Set(ref _activeGuid, value); }
    public bool HasPlans { get => _hasPlans; private set => Set(ref _hasPlans, value); }
    public bool IsEmpty => !HasPlans && !IsBusy;
    public string BundledCountText => $"({BundledPlans.Count} READY-MADE)";

    public override async Task LoadAsync()
    {
        IsBusy = true;
        BusyText = "READING INSTALLED POWER PLANS...";
        try
        {
            var plans = await _power.ListAsync();
            Plans.Clear();
            foreach (var plan in plans)
            {
                Plans.Add(new PowerPlanRowViewModel(plan, ActivateAsync));
            }

            _planCount = plans.Count;
            CountText = $"({_planCount} INSTALLED)";
            HasPlans = plans.Count > 0;
            Raise(nameof(IsEmpty));

            var active = plans.FirstOrDefault(p => p.IsActive) ?? plans.FirstOrDefault();
            ActiveName = active?.Name ?? "NO POWER PLAN FOUND";
            ActiveGuid = active?.Guid ?? "—";
        }
        catch (Exception ex)
        {
            Plans.Clear();
            HasPlans = false;
            CountText = "(UNAVAILABLE)";
            ActiveName = "COULD NOT READ POWER PLANS";
            ActiveGuid = "—";
            Raise(nameof(IsEmpty));
            _toast("POWER PLANS UNAVAILABLE", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
            Raise(nameof(IsEmpty));
        }
    }

    private async Task ActivateAsync(PowerPlan plan)
    {
        var result = await _power.ActivateAsync(plan.Guid);
        if (!result.Ok)
        {
            _toast("ACTIVATION FAILED", result.Error);
            return;
        }

        _toast("POWER PLAN ACTIVATED", plan.Name);
        await LoadAsync();
    }

    private async Task ApplyBundledAsync(BundledPowerPlan plan)
    {
        var result = await _power.ApplyBundledAsync(plan.Id);
        if (!result.Ok)
        {
            _toast("PLAN APPLY FAILED", result.Error);
            return;
        }

        _toast("POWER PLAN ACTIVATED", plan.Name);
        await LoadAsync();
    }
}
