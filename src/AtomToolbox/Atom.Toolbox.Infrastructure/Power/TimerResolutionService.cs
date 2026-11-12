using System.Runtime.InteropServices;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Power;

namespace Atom.Toolbox.Infrastructure.Power;

/// <summary>
/// Holds a fast system timer for the lifetime of the process. Windows releases the request
/// automatically when AtomToolBox exits, so a stuck helper process is impossible by design.
/// </summary>
public sealed class TimerResolutionService : ITimerResolutionService, IDisposable
{
    private readonly object _gate = new();
    private bool _held;
    private double _heldMs;

    public TimerResolutionStatus GetStatus()
    {
        if (NtQueryTimerResolution(out var slowest, out var fastest, out var current) != 0)
        {
            return TimerResolutionStatus.Unavailable;
        }

        lock (_gate)
        {
            return new TimerResolutionStatus(
                Math.Round(current / 10000d, 3),
                Math.Round(fastest / 10000d, 3),
                Math.Round(slowest / 10000d, 3),
                _held,
                _held ? _heldMs : null);
        }
    }

    public Task<OperationResult> HoldAsync(double milliseconds, CancellationToken cancellationToken = default)
    {
        var target = Math.Clamp(milliseconds, 0.5, 15.625);
        var units = (uint)Math.Round(target * 10000);

        var status = NtSetTimerResolution(units, true, out var actual);
        if (status != 0)
        {
            return Task.FromResult(OperationResult.Fail($"The system rejected the timer request (NTSTATUS 0x{status:X8})."));
        }

        lock (_gate)
        {
            _held = true;
            _heldMs = Math.Round(actual / 10000d, 3);
        }
        return Task.FromResult(OperationResult.Success);
    }

    public Task<OperationResult> ReleaseAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_held)
            {
                return Task.FromResult(OperationResult.Success);
            }

            var status = NtSetTimerResolution(0, false, out _);
            _held = false;
            _heldMs = 0;
            return Task.FromResult(status == 0
                ? OperationResult.Success
                : OperationResult.Fail("Windows did not release the timer request."));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_held)
            {
                NtSetTimerResolution(0, false, out _);
                _held = false;
            }
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(out uint maximumTime, out uint minimumTime, out uint currentTime);

    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.U1)] bool setResolution, out uint currentResolution);
}
