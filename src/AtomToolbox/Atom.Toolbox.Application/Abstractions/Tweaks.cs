using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Tweaks;

namespace Atom.Toolbox.Application.Abstractions;

public interface ITweakExecutor
{
    Task<OperationResult> ApplyAsync(TweakDefinition tweak, bool enable, IProgress<string>? log = null, CancellationToken cancellationToken = default);
    Task<TweakCheckResult> VerifyAsync(TweakDefinition tweak, CancellationToken cancellationToken = default);
}

public interface ITweakStateStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    bool IsOn(string pageId, string tweakId);
    void Set(string pageId, string tweakId, bool isOn);
    int ActiveCount { get; }
    IReadOnlyDictionary<string, bool> Snapshot();
    void Clear();
}

public sealed record TweakToggleResult(bool Ok, bool Enabled, string? Error = null);

public interface ITweakService
{
    Task<TweakToggleResult> ToggleAsync(TweakPage page, TweakDefinition tweak, bool enable, IProgress<string>? log = null, CancellationToken cancellationToken = default);
    Task<TweakCheckResult> VerifyAsync(TweakDefinition tweak, CancellationToken cancellationToken = default);
    Task<int> VerifyPageAsync(TweakPage page, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    bool IsOn(TweakPage page, TweakDefinition tweak);
}
