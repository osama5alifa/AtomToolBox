using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Power;
using Atom.Toolbox.Domain.System;

namespace Atom.Toolbox.Application.Abstractions;

public interface ISystemProbe
{
    Task<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface IPowerPlanService
{
    Task<IReadOnlyList<PowerPlan>> ListAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> ActivateAsync(string guid, CancellationToken cancellationToken = default);
    Task<OperationResult<string?>> ImportAsync(string file, CancellationToken cancellationToken = default);
    Task<OperationResult<PowerPlan>> ApplyBundledAsync(string planId, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteAsync(string guid, CancellationToken cancellationToken = default);
    Task<OperationResult> RenameAsync(string guid, string name, string? description, CancellationToken cancellationToken = default);
    Task<OperationResult<PowerPlan>> DuplicateAsync(string guid, string name, string? description, CancellationToken cancellationToken = default);
    Task<OperationResult<long>> ExportAsync(string guid, string file, CancellationToken cancellationToken = default);
    Task<OperationResult<PowerSchemeDetails>> ReadSchemeAsync(string guid, CancellationToken cancellationToken = default);
    Task<OperationResult<string?>> EnsureSchemeAsync(string templateGuid, string friendlyName, CancellationToken cancellationToken = default);
    Task<PowerChangeResult> SetValueAsync(
        string guid, string subgroup, string setting, int ac, int dc, int? min, int? max,
        CancellationToken cancellationToken = default);
}

public interface ITimerResolutionService
{
    TimerResolutionStatus GetStatus();
    Task<OperationResult> HoldAsync(double milliseconds, CancellationToken cancellationToken = default);
    Task<OperationResult> ReleaseAsync(CancellationToken cancellationToken = default);
}
