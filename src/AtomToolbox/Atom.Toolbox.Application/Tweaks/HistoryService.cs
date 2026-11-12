using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Application.Tweaks;

/// <summary>Append-only undo history with a 200 entry ceiling, mirroring the legacy build.</summary>
public sealed class HistoryService(
    IHistoryStore store,
    ITweakCatalog catalog,
    ITweakExecutor executor,
    ITweakStateStore state,
    IStartupService startup,
    IProcessRunner processes) : IHistoryService
{
    private const int MaxEntries = 200;
    private readonly List<HistoryEntry> _entries = new(store.Load());

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public void Add(HistoryEntry entry)
    {
        _entries.Insert(0, entry);
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }
        store.Save(_entries);
    }

    public void Clear()
    {
        _entries.Clear();
        store.Save(_entries);
    }

    public async Task<OperationResult> RevertAsync(
        HistoryEntry entry,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (entry.Category == "startup")
        {
            var scan = await startup.ScanAsync(cancellationToken).ConfigureAwait(false);
            var item = scan.Entries.FirstOrDefault(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                return OperationResult.Fail("The startup entry no longer exists.");
            }

            var result = await startup.SetEnabledAsync(item, !entry.Applied, cancellationToken).ConfigureAwait(false);
            return result is { Ok: true, Verified: true }
                ? OperationResult.Success
                : OperationResult.Fail(result.Error ?? "The startup change could not be verified.");
        }

        if (entry.PageId is not null)
        {
            var tweak = catalog.FindTweak(entry.PageId, entry.Id);
            if (tweak is null)
            {
                return OperationResult.Fail("This tweak is not part of the current catalog.");
            }

            var result = await executor.ApplyAsync(tweak, !entry.Applied, log, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                return result;
            }

            state.Set(entry.PageId, entry.Id, !entry.Applied);
            log?.Report($"{entry.Name}: {(entry.Applied ? "REVERTED" : "RE-APPLIED")}");
            return OperationResult.Success;
        }

        if (!string.IsNullOrWhiteSpace(entry.RevertToken))
        {
            var command = entry.RevertToken.StartsWith("cmd:", StringComparison.OrdinalIgnoreCase)
                ? entry.RevertToken[4..]
                : entry.RevertToken;

            var result = await processes.RunAsync(new ProcessRequest(
                "cmd.exe",
                ["/d", "/s", "/c", command],
                Timeout: TimeSpan.FromSeconds(60)), cancellationToken).ConfigureAwait(false);

            return result.Ok
                ? OperationResult.Success
                : OperationResult.Fail(result.StandardError.Length > 0 ? result.StandardError : "The revert command failed.");
        }

        return OperationResult.Fail("This entry has no revert data.");
    }
}
