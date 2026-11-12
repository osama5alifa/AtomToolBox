using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Domain.Tweaks;

namespace Atom.Toolbox.Application.Tweaks;

/// <summary>
/// Orchestrates a tweak change: execute the typed actions, persist the new state and append
/// an audit entry. It deliberately owns no Windows API knowledge.
/// </summary>
public sealed class TweakService(
    ITweakExecutor executor,
    ITweakStateStore state,
    IHistoryService history,
    ILicenseService license) : ITweakService
{
    public bool IsOn(TweakPage page, TweakDefinition tweak) => state.IsOn(page.Id, tweak.Id);

    public async Task<TweakToggleResult> ToggleAsync(
        TweakPage page,
        TweakDefinition tweak,
        bool enable,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!page.IsFree && !license.IsPro)
        {
            return new TweakToggleResult(false, !enable, "This module is part of Atom Optimizer.");
        }

        try
        {
            log?.Report($"{(enable ? "Applying" : "Reverting")}: {tweak.Name}");
            var result = await executor.ApplyAsync(tweak, enable, log, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                return new TweakToggleResult(false, !enable, result.Error ?? "The change could not be applied.");
            }

            state.Set(page.Id, tweak.Id, enable);
            history.Add(new HistoryEntry
            {
                Id = tweak.Id,
                Name = tweak.Name,
                Category = page.Id,
                Applied = enable,
                Timestamp = DateTimeOffset.Now,
                PageId = page.Id
            });

            log?.Report($"{tweak.Name}: {(enable ? "APPLIED" : "REVERTED")}");
            return new TweakToggleResult(true, enable);
        }
        catch (OperationCanceledException)
        {
            return new TweakToggleResult(false, !enable, "Cancelled.");
        }
        catch (Exception ex)
        {
            log?.Report($"{tweak.Name}: ERROR — {ex.Message}");
            return new TweakToggleResult(false, !enable, ex.Message);
        }
    }

    public async Task<TweakCheckResult> VerifyAsync(TweakDefinition tweak, CancellationToken cancellationToken = default) =>
        await executor.VerifyAsync(tweak, cancellationToken).ConfigureAwait(false);

    public async Task<int> VerifyPageAsync(TweakPage page, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var active = 0;
        var done = 0;

        // Native read-backs are pure registry access: run them with high parallelism.
        var native = page.Tweaks.Where(t => t.SupportsNativeVerification).ToList();
        var legacy = page.Tweaks.Where(t => !t.SupportsNativeVerification).ToList();

        await Parallel.ForEachAsync(
            native,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken },
            async (tweak, token) =>
            {
                var result = await executor.VerifyAsync(tweak, token).ConfigureAwait(false);
                if (result.Error is null)
                {
                    state.Set(page.Id, tweak.Id, result.Enabled);
                    if (result.Enabled) Interlocked.Increment(ref active);
                }
                progress?.Report(Interlocked.Increment(ref done));
            }).ConfigureAwait(false);

        // Legacy checks spawn a shell: keep concurrency low so old machines stay responsive.
        await Parallel.ForEachAsync(
            legacy,
            new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellationToken },
            async (tweak, token) =>
            {
                var result = await executor.VerifyAsync(tweak, token).ConfigureAwait(false);
                if (result.Error is null)
                {
                    state.Set(page.Id, tweak.Id, result.Enabled);
                    if (result.Enabled) Interlocked.Increment(ref active);
                }
                progress?.Report(Interlocked.Increment(ref done));
            }).ConfigureAwait(false);

        return active;
    }
}
