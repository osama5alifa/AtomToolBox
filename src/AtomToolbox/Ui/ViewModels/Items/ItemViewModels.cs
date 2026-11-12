using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Atom.Toolbox.Desktop.ViewModels.Items;

public enum StatusTone
{
    Neutral,
    On,
    Off,
    Working,
    Failed
}

public sealed class PageSectionViewModel
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<object> Items { get; init; } = Array.Empty<object>();
    public bool ShowTitle => Title.Length > 0;
    public bool ShowDescription => Description.Length > 0;
    public bool IsGrid { get; init; }
    public bool IsCard { get; init; }
    public System.Windows.Thickness Margin { get; init; } = new(0, 0, 0, 14);
}

public sealed class MessageViewModel
{
    public required string Text { get; init; }
}


public abstract class ItemViewModel : ObservableObject
{
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        protected set
        {
            if (Set(ref _isBusy, value))
            {
                Raise(nameof(CanAct));
            }
        }
    }

    public bool CanAct => !IsBusy;
}

public sealed class ToggleRowViewModel : ItemViewModel
{
    private readonly Func<bool, Task<ToggleOutcome>> _toggle;
    private bool _isOn;
    private string _statusText = "OFF";
    private StatusTone _tone = StatusTone.Off;
    private ICommand? _toggleCommand;

    public ToggleRowViewModel(string name, string description, bool isOn, Func<bool, Task<ToggleOutcome>> toggle)
    {
        Name = name;
        Description = description;
        _isOn = isOn;
        _statusText = isOn ? "ON" : "OFF";
        _tone = isOn ? StatusTone.On : StatusTone.Off;
        _toggle = toggle;
    }

    public string Name { get; }
    public string Description { get; }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (Set(ref _isOn, value))
            {
                StatusText = value ? "ON" : "OFF";
                Tone = value ? StatusTone.On : StatusTone.Off;
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public StatusTone Tone
    {
        get => _tone;
        private set => Set(ref _tone, value);
    }

    public ICommand ToggleCommand => _toggleCommand ??= new AsyncRelayCommand(ToggleAsync);

    private async Task ToggleAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var desired = IsOn;
        IsBusy = true;
        StatusText = "...";
        Tone = StatusTone.Working;
        try
        {
            var result = await _toggle(desired).ConfigureAwait(true);
            if (result.Ok)
            {
                StatusText = "OK";
                Tone = StatusTone.On;
                await Task.Delay(1800).ConfigureAwait(true);
            }
            else
            {
                IsOn = !desired;
                StatusText = result.AdminRequired ? "ADMIN" : "FAILED";
                Tone = StatusTone.Failed;
                await Task.Delay(2600).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("toggle", ex);
            IsOn = !desired;
            StatusText = "FAILED";
            Tone = StatusTone.Failed;
            await Task.Delay(2600).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            StatusText = IsOn ? "ON" : "OFF";
            Tone = IsOn ? StatusTone.On : StatusTone.Off;
        }
    }
}

public sealed record ToggleOutcome(bool Ok, bool AdminRequired = false, string? Error = null)
{
    public static ToggleOutcome Success { get; } = new(true);
    public static ToggleOutcome Fail(bool adminRequired = false, string? error = null) => new(false, adminRequired, error);
}

public sealed class ActionRowViewModel : ItemViewModel
{
    private readonly Func<Task<ActionOutcome>>? _execute;
    private string _actionText;
    private string _statusText = "";
    private StatusTone _tone = StatusTone.Neutral;

    public ActionRowViewModel(string name, string description, string actionText, Func<Task<ActionOutcome>>? execute)
    {
        Name = name;
        Description = description;
        _actionText = actionText;
        _execute = execute;
        Command = new AsyncRelayCommand(RunAsync);
    }

    public string Name { get; }
    public string Description { get; }
    public ICommand Command { get; }
    public bool HasAction => _execute is not null;

    public string ActionText
    {
        get => _actionText;
        private set => Set(ref _actionText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public StatusTone Tone
    {
        get => _tone;
        private set => Set(ref _tone, value);
    }

    private async Task RunAsync()
    {
        if (_execute is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        var original = ActionText;
        ActionText = "...";
        StatusText = "";
        try
        {
            var result = await _execute().ConfigureAwait(true);
            StatusText = result.Message ?? (result.Ok ? "DONE" : "FAILED");
            Tone = result.Ok ? StatusTone.On : StatusTone.Failed;
            await Task.Delay(result.Ok ? 2000 : 3000).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            ActionText = original;
            StatusText = "";
            Tone = StatusTone.Neutral;
        }
    }
}

public sealed record ActionOutcome(bool Ok, string? Message = null);

public sealed class ProcessRowViewModel : ItemViewModel
{
    private readonly Func<Task<ActionOutcome>> _kill;
    private readonly Func<Task<ActionOutcome>> _block;
    private string _statusText = "";
    private StatusTone _tone = StatusTone.Neutral;

    public ProcessRowViewModel(int pid, string name, string memory, bool isProtected,
        Func<Task<ActionOutcome>> kill, Func<Task<ActionOutcome>> block)
    {
        Pid = pid;
        Name = name;
        Memory = memory;
        IsProtected = isProtected;
        _kill = kill;
        _block = block;
        KillCommand = new AsyncRelayCommand(() => RunAsync(_kill));
        BlockCommand = new AsyncRelayCommand(() => RunAsync(_block));
    }

    public int Pid { get; }
    public string Name { get; }
    public string Memory { get; }
    public bool IsProtected { get; }
    public bool CanControl => !IsProtected && !IsBusy;
    public ICommand KillCommand { get; }
    public ICommand BlockCommand { get; }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public StatusTone Tone { get => _tone; private set => Set(ref _tone, value); }

    private async Task RunAsync(Func<Task<ActionOutcome>> action)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            var result = await action().ConfigureAwait(true);
            StatusText = result.Message ?? (result.Ok ? "OK" : "FAILED");
            Tone = result.Ok ? StatusTone.On : StatusTone.Failed;
            await Task.Delay(2000).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            StatusText = "";
            Tone = StatusTone.Neutral;
        }
    }
}

public sealed class ActionButtonViewModel : ObservableObject
{
    private bool _isActive;
    private bool _isEnabled = true;

    public ActionButtonViewModel(string label, ICommand command, bool isActive = false)
    {
        Label = label;
        Command = command;
        _isActive = isActive;
    }

    public string Label { get; }
    public ICommand Command { get; }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }
}

public sealed class SettingsRowViewModel
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string TagText { get; init; } = "";
    public IReadOnlyList<ActionButtonViewModel> Actions { get; init; } = Array.Empty<ActionButtonViewModel>();
    public bool HasTag => TagText.Length > 0;
    public bool HasActions => Actions.Count > 0;
}

public sealed class ButtonGridItemViewModel
{
    public required string Label { get; init; }
    public required ICommand Command { get; init; }
}

public sealed class AppRowViewModel : ItemViewModel
{
    private readonly Func<Task<ActionOutcome>> _install;
    private string _actionText = "INSTALL";
    private string _statusText = "";

    public AppRowViewModel(string id, string name, Func<Task<ActionOutcome>> install)
    {
        Id = id;
        Name = name;
        _install = install;
        Command = new AsyncRelayCommand(InstallAsync);
    }

    public string Id { get; }
    public string Name { get; }
    public ICommand Command { get; }
    public string ActionText { get => _actionText; private set => Set(ref _actionText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private async Task InstallAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        ActionText = "INSTALLING...";
        try
        {
            var result = await _install().ConfigureAwait(true);
            ActionText = result.Ok ? "INSTALLED" : "FAILED";
            StatusText = result.Message ?? "";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class ConsoleViewModel : ObservableObject
{
    private const int MaxLines = 400;
    private string _text = "-- Ready --";

    public ConsoleViewModel()
    {
        ClearCommand = new RelayCommand(Clear);
    }

    public string Text
    {
        get => _text;
        private set => Set(ref _text, value);
    }

    public ICommand ClearCommand { get; }

    public void Write(string message)
    {
        var line = message.StartsWith('>') || message.StartsWith('[') ? message : "> " + message;
        var lines = Text == "-- Ready --" || Text == "-- No active tweaks --"
            ? new List<string>()
            : Text.Split('\n').ToList();
        lines.Add(line);
        if (lines.Count > MaxLines)
        {
            lines.RemoveRange(0, lines.Count - MaxLines);
        }
        Text = string.Join('\n', lines);
    }

    public void Reset(string placeholder = "-- Ready --") => Text = placeholder;

    public void Clear() => Text = "-- Ready --";
}

public sealed class MetricCardViewModel : ObservableObject
{
    private string _value = "--";
    private double _percent;
    private string _sub = "";

    public required string Label { get; init; }

    public string Value { get => _value; set => Set(ref _value, value); }
    public double Percent { get => _percent; set => Set(ref _percent, value); }
    public string Sub { get => _sub; set => Set(ref _sub, value); }
    public bool HasBar { get; init; }
}

public sealed class KeyValueViewModel
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}

public sealed class BusyViewModel : ObservableObject
{
    private bool _isBusy;
    private string _message = "";

    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
}

public class PageViewModel : ObservableObject
{
    private string _title = "";
    private string _countText = "";
    private string _intro = "";
    private bool _showAdminWarning;
    private bool _isBusy;
    private string _busyText = "";
    private bool _showPreview;
    private string _previewTitle = "";
    private string _previewText = "";
    private bool _showProgress;
    private double _progressPercent;
    private string _progressText = "";

    public string Title { get => _title; protected set => Set(ref _title, value); }
    public string CountText { get => _countText; protected set => Set(ref _countText, value); }
    public string Intro { get => _intro; protected set => Set(ref _intro, value); }
    public bool ShowAdminWarning { get => _showAdminWarning; protected set => Set(ref _showAdminWarning, value); }
    public bool IsBusy { get => _isBusy; protected set { if (Set(ref _isBusy, value)) Raise(nameof(CanInteract)); } }
    public string BusyText { get => _busyText; protected set => Set(ref _busyText, value); }
    public bool CanInteract => !IsBusy;

    public bool ShowPreview { get => _showPreview; protected set => Set(ref _showPreview, value); }
    public string PreviewTitle { get => _previewTitle; protected set => Set(ref _previewTitle, value); }
    public string PreviewText { get => _previewText; protected set => Set(ref _previewText, value); }

    public bool ShowProgress { get => _showProgress; protected set => Set(ref _showProgress, value); }
    public double ProgressPercent { get => _progressPercent; protected set => Set(ref _progressPercent, value); }
    public string ProgressText { get => _progressText; protected set => Set(ref _progressText, value); }

    public ObservableCollection<PageSectionViewModel> Sections { get; } = new();
    public ObservableCollection<ActionButtonViewModel> Toolbar { get; } = new();
    public ConsoleViewModel Console { get; } = new();
    public bool HasConsole { get; protected set; } = true;

    public virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Called when the shell navigates away; pages stop timers here.</summary>
    public virtual void Deactivate()
    {
    }
}
