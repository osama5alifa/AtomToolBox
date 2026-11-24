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
    Task<FixResult> RepairWindowsUpdateAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairComponentStoreAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairWmiAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairStoreAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairAudioAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairPrintAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairShellAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairSecurityAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairTimeAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairDevicesAsync(CancellationToken cancellationToken = default);
    Task<FixResult> RepairGamingAsync(CancellationToken cancellationToken = default);
}
