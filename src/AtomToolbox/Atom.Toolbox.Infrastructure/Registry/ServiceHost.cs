using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Atom.Toolbox.Infrastructure.Registry;

/// <summary>
/// Service control through advapi32 rather than shelling out to sc.exe. This is the same
/// API Task Manager and services.msc use, so changes apply without a console round trip.
/// </summary>
public sealed class ServiceHost
{
    public void SetStartup(string serviceName, string startup)
    {
        using var handle = OpenService(serviceName, ServiceAccess.ChangeConfig);
        var startType = startup.ToLowerInvariant() switch
        {
            "disabled" => 4u,
            "manual" => 3u,
            "automatic" => 2u,
            "delayed" => 2u,
            _ => throw new ArgumentOutOfRangeException(nameof(startup), startup, "Unsupported service start mode.")
        };

        if (!ChangeServiceConfig(handle.DangerousGetHandle(), 0xFFFFFFFF, startType, 0xFFFFFFFF,
                null, null, IntPtr.Zero, null, null, null, null))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not set the startup mode of '{serviceName}'.");
        }

        if (startup.Equals("delayed", StringComparison.OrdinalIgnoreCase))
        {
            var delayed = new ServiceDelayedAutoStart { Delayed = true };
            ChangeServiceConfig2(handle.DangerousGetHandle(), 3, ref delayed);
        }
    }

    public void Stop(string serviceName) => Control(serviceName, stop: true);

    public void Start(string serviceName) => Control(serviceName, stop: false);

    private void Control(string serviceName, bool stop)
    {
        using var handle = OpenService(serviceName, ServiceAccess.Stop | ServiceAccess.Start | ServiceAccess.QueryStatus);
        var control = stop ? 1u : 0u;
        if (!ControlService(handle.DangerousGetHandle(), control, out _))
        {
            var error = Marshal.GetLastWin32Error();
            // 1062 = service not started, 1056 = already running.
            if ((stop && error == 1062) || (!stop && error == 1056))
            {
                return;
            }
            throw new Win32Exception(error, $"Could not {(stop ? "stop" : "start")} service '{serviceName}'.");
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (!QueryServiceStatus(handle.DangerousGetHandle(), out var status))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not query service '{serviceName}'.");
            }

            var state = status.CurrentState;
            if (stop && state == 1) return;
            if (!stop && state == 4) return;
            if (state == 1 && !stop) return;

            Thread.Sleep(150);
        }
    }

    private static SafeServiceHandle OpenService(string name, ServiceAccess access)
    {
        var manager = OpenSCManager(null, null, 0x0001);
        if (manager.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the service control manager.");
        }

        try
        {
            var service = OpenService(manager, name, access);
            if (service.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open service '{name}'.");
            }
            return service;
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Flags]
    private enum ServiceAccess : uint
    {
        QueryStatus = 0x0004,
        Stop = 0x0020,
        Start = 0x0010,
        ChangeConfig = 0x0002
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceDelayedAutoStart
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool Delayed;
    }

    private sealed class SafeServiceHandle : SafeHandle
    {
        public SafeServiceHandle() : base(IntPtr.Zero, true) { }
        public override bool IsInvalid => handle == IntPtr.Zero;
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")]
    private static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string name, ServiceAccess access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(
        IntPtr service, uint serviceType, uint startType, uint errorControl,
        string? binaryPath, string? loadOrderGroup, IntPtr tagId,
        string? dependencies, string? account, string? password, string? displayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref ServiceDelayedAutoStart info);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr service, int argc, string[]? argv);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "ControlService")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "QueryServiceStatus")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
