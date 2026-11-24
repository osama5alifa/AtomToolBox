using Atom.Toolbox.Domain.Common;

namespace Atom.Toolbox.Application.Abstractions;

public sealed record FixResult(bool Ok, string Message)
{
    public static FixResult Success(string message) => new(true, message);
    public static FixResult Fail(string message) => new(false, message);
}

public interface ISystemFixService
{
    Task<FixResult> RepairDisplayAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairSearchAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairBluetoothAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairNvidiaAsync(CancellationToken cancellationToken = default);
    Task<FixResult> OpenMicrophoneAsync(CancellationToken cancellationToken = default);
    Task<FixResult> ResetNetworkAsync(CancellationToken cancellationToken = default);
    Task<FixResult> EnablePageFileAsync(CancellationToken cancellationToken = default);
}
