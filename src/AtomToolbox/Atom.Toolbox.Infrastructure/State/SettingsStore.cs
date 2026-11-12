using Atom.Toolbox.Application.Abstractions;

namespace Atom.Toolbox.Infrastructure.State;

public sealed class SettingsStore : ISettingsStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private AppSettings _current;

    public SettingsStore(IEnvironmentService environment)
    {
        _path = Path.Combine(environment.LocalAppDataDirectory, "settings.json");
        _current = JsonFile.Read(_path, new AppSettings(AppTheme.Dark, SmoothScrolling: true));
    }

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler? Changed;

    public string ActiveProfile => "Windows_NT";

    public void SetTheme(AppTheme theme)
    {
        Update(_current with { Theme = theme });
    }

    public void SetSmoothScrolling(bool enabled)
    {
        Update(_current with { SmoothScrolling = enabled });
    }

    private void Update(AppSettings next)
    {
        lock (_gate)
        {
            if (_current == next)
            {
                return;
            }
            _current = next;
            JsonFile.Write(_path, _current);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
