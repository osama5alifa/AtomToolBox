using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Infrastructure.State;

public sealed class HistoryStore(IEnvironmentService environment) : IHistoryStore
{
    private readonly string _path = Path.Combine(environment.LocalAppDataDirectory, "history.json");

    public IReadOnlyList<HistoryEntry> Load() => JsonFile.Read<List<HistoryEntry>>(_path, []);

    public void Save(IReadOnlyList<HistoryEntry> entries) => JsonFile.Write(_path, entries);
}
