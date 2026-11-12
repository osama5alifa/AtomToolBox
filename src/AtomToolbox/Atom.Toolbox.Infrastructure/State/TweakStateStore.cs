using Atom.Toolbox.Application.Abstractions;

namespace Atom.Toolbox.Infrastructure.State;

/// <summary>
/// Tweak on/off memory, keyed by "page/tweak". Writes are debounced because a page
/// state scan can touch dozens of entries in one burst.
/// </summary>
public sealed class TweakStateStore : ITweakStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, bool> _states;
    private readonly string _path;
    private CancellationTokenSource? _pendingSave;

    public TweakStateStore(IEnvironmentService environment)
    {
        _path = Path.Combine(environment.LocalAppDataDirectory, "tweak-state.json");
        _states = JsonFile.Read<Dictionary<string, bool>>(_path, []);
    }

    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _states.Count(kv => kv.Value);
            }
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public bool IsOn(string pageId, string tweakId)
    {
        lock (_gate)
        {
            return _states.TryGetValue(Key(pageId, tweakId), out var value) && value;
        }
    }

    public void Set(string pageId, string tweakId, bool isOn)
    {
        lock (_gate)
        {
            _states[Key(pageId, tweakId)] = isOn;
            ScheduleSave();
        }
    }

    public IReadOnlyDictionary<string, bool> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, bool>(_states);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _states.Clear();
            ScheduleSave();
        }
    }

    private void ScheduleSave()
    {
        _pendingSave?.Cancel();
        _pendingSave?.Dispose();
        var source = new CancellationTokenSource();
        _pendingSave = source;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(700, source.Token).ConfigureAwait(false);
                Dictionary<string, bool> copy;
                lock (_gate)
                {
                    copy = new Dictionary<string, bool>(_states);
                }
                JsonFile.Write(_path, copy);
            }
            catch (OperationCanceledException)
            {
                // A newer write superseded this one.
            }
        });
    }

    private static string Key(string pageId, string tweakId) => pageId + "/" + tweakId;
}
