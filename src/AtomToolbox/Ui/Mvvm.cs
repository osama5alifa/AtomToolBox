using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Atom.Toolbox.Desktop.Ui;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Synchronous command with optional re-entrancy guard and UI exception funnel.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        try
        {
            _execute(parameter);
        }
        catch (Exception ex)
        {
            AppLog.Error("command", ex);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Async command that disables itself while running. Every failure is logged; optional
/// handlers surface user-facing errors through the shell toast.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute(), onError)
    {
    }

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        catch (OperationCanceledException)
        {
            // Commands may be cancelled by navigation; not an error.
        }
        catch (Exception ex)
        {
            AppLog.Error("async-command", ex);
            _onError?.Invoke(ex);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>File log for diagnostics; never throws into the UI.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = ResolveLogDirectory();

    private static string ResolveLogDirectory()
    {
        var root = Environment.GetEnvironmentVariable("ATOM_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            return Path.Combine(root, "AtomToolBox", "logs");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomToolBox", "logs");
    }

    public static void Error(string context, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"[{DateTimeOffset.Now:O}] {context}:{Environment.NewLine}{exception}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(LogDirectory, "app.log"), line);
            }
        }
        catch (Exception)
        {
            // Logging must never crash the application.
        }
    }

    public static void Info(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(LogDirectory, "app.log"),
                    $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Ignore.
        }
    }
}
