using System.Runtime.InteropServices;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.System;
using Atom.Toolbox.Infrastructure.Registry;

namespace Atom.Toolbox.Infrastructure.SystemInfo;

/// <summary>
/// Hardware and OS facts from Win32 only. No WMI, no PowerShell, no performance counters:
/// a snapshot costs a handful of syscalls and never blocks for more than the first CPU sample.
/// </summary>
public sealed class SystemProbe(IEnvironmentService environment) : ISystemProbe
{
    private readonly object _cpuGate = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasCpuSample;
    private int _lastCpu;
    private long _lastCpuSampleTicks;

    private string? _gpu;
    private string? _cpuName;
    private string? _osName;
    private string? _osBuild;
    private string? _osVersion;

    public async Task<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        await _captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CaptureCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private async Task<SystemSnapshot> CaptureCoreAsync(CancellationToken cancellationToken)
    {
        if (!_hasCpuSample)
        {
            SampleCpu(out var firstIdle, out var firstKernel, out var firstUser, out _);
            lock (_cpuGate)
            {
                _previousIdle = firstIdle;
                _previousKernel = firstKernel;
                _previousUser = firstUser;
                _hasCpuSample = true;
            }
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }

        // Several callers (header + dashboard) can ask at once. Sampling over a few
        // milliseconds produces meaningless spikes, so reuse the last reading briefly.
        if (Environment.TickCount64 - _lastCpuSampleTicks >= 700)
        {
            SampleCpu(out var idle, out var kernel, out var user, out _);
            lock (_cpuGate)
            {
                var idleDelta = idle - _previousIdle;
                var totalDelta = (kernel - _previousKernel) + (user - _previousUser);
                var busy = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
                if (totalDelta > 0)
                {
                    _lastCpu = (int)Math.Clamp(Math.Round(busy * 100d / totalDelta), 0, 100);
                }

                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;
                _lastCpuSampleTicks = Environment.TickCount64;
            }
        }

        var memory = ReadMemory();
        var drive = ReadSystemDrive();
        LoadStaticFacts();

        return new SystemSnapshot(
            _lastCpu,
            memory.UsedPercent,
            memory.Total,
            memory.Free,
            drive.Total,
            drive.Free,
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            _gpu ?? "N/A",
            _cpuName ?? "CPU",
            _osName ?? "Windows",
            _osVersion ?? "10.0",
            _osBuild ?? "--",
            Environment.MachineName,
            Environment.ProcessorCount,
            environment.IsElevated);
    }

    private int SampleCpu(out ulong idle, out ulong kernel, out ulong user, out ulong total)
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            idle = kernel = user = total = 0;
            return _lastCpu;
        }

        idle = ToUInt64(idleTime);
        kernel = ToUInt64(kernelTime);
        user = ToUInt64(userTime);
        total = kernel + user;
        return _lastCpu;
    }

    private static (int UsedPercent, long Total, long Free) ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return (0, 0, 0);
        }

        var used = status.TotalPhysical > 0
            ? (int)Math.Round((status.TotalPhysical - status.AvailablePhysical) * 100d / status.TotalPhysical)
            : 0;
        return (used, (long)status.TotalPhysical, (long)status.AvailablePhysical);
    }

    private static (long Total, long Free) ReadSystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            return (drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private void LoadStaticFacts()
    {
        _gpu ??= ReadGpuName();
        if (_cpuName is null || _osName is null || _osBuild is null || _osVersion is null)
        {
            var store = new RegistryStore();
            var cpu = ReadString(store, "HKLM",
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
            _cpuName ??= string.IsNullOrWhiteSpace(cpu) ? "CPU" : cpu.Trim();

            var product = ReadString(store, "HKLM",
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Windows";
            var build = ReadString(store, "HKLM",
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild") ?? "0";
            var ubr = ReadString(store, "HKLM",
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");

            _osName = product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase) &&
                      int.TryParse(build, out var buildNumber) && buildNumber >= 22000
                ? "Windows 11"
                : product;

            var version = Environment.OSVersion.Version;
            _osVersion = $"{version.Major}.{version.Minor}.{version.Build}";
            _osBuild = $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private static string ReadGpuName()
    {
        for (uint index = 0; index < 8; index++)
        {
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 1))
            {
                break;
            }

            var name = device.DeviceString.Trim();
            if (name.Length == 0 || name.StartsWith("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return name;
        }
        return "N/A";
    }

    private static string? ReadString(RegistryStore store, string hive, string path, string name) =>
        store.TryRead(hive, path, name, out var value) ? value.AsText().Trim() : null;

    private static ulong ToUInt64(FileTime time) => ((ulong)(uint)time.HighDateTime << 32) | time.LowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;

        public DisplayDevice()
        {
            DeviceName = string.Empty;
            DeviceString = string.Empty;
            DeviceId = string.Empty;
            DeviceKey = string.Empty;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint deviceIndex, ref DisplayDevice displayDevice, uint flags);
}
