using System.ComponentModel;
using System.Runtime.InteropServices;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Infrastructure.Processes;
using Atom.Toolbox.Infrastructure.Registry;

namespace Atom.Toolbox.Infrastructure.Fixes;

/// <summary>
/// Repairs for Windows subsystems that fail outside the normal tweak catalog.
/// Each operation is explicit, bounded by a timeout, and reports a short result
/// the UI can show without hiding the underlying error text in logs.
/// </summary>
public sealed class SystemFixService(
    RegistryStore registry,
    ServiceHost services,
    IProcessRunner processes,
    IEnvironmentService environment) : ISystemFixService
{
    private const string GraphicsPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    public Task<FixResult> RepairDisplayAsync(CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        foreach (var key in new[] { "Configuration", "Connectivity", "ScaleFactors" })
        {
            try
            {
                registry.DeleteKeyTree("HKLM", GraphicsPath + @"\" + key);
            }
            catch (Exception ex)
            {
                notes.Add(key + ": " + ex.Message);
            }
        }

        try
        {
            registry.SetDWord("HKLM", GraphicsPath, "TdrDelay", 10);
            registry.SetDWord("HKLM", GraphicsPath, "DisableDynamicPstate", 1);
            notes.Add("TDR delay and dynamic P-state updated");
        }
        catch (Exception ex)
        {
            notes.Add("Graphics policy: " + ex.Message);
        }

        try
        {
            services.SetStartup("DispBrokerDesktopSvc", "automatic");
            services.Start("DispBrokerDesktopSvc");
            notes.Add("Display broker running");
        }
        catch (Exception ex)
        {
            notes.Add("Display broker: " + ex.Message);
        }

        var count = PersistCurrentDisplayModes();
        if (count > 0)
        {
            notes.Add("Saved the current mode for " + count + " display" + (count == 1 ? "" : "s"));
        }
        else
        {
            notes.Add("Could not read an active display mode");
        }

        var ok = count > 0;
        return Task.FromResult(ok
            ? FixResult.Success("Display mode saved. Restart if it still drops to 60 Hz.")
            : FixResult.Fail(string.Join(". ", notes)));
    }

    public Task<FixResult> RepairSearchAsync(CancellationToken cancellationToken = default) =>
        RunScriptAsync(SearchScript, "Search services restarted and the search packages re-registered.", cancellationToken);

    public Task<FixResult> RepairBluetoothAsync(CancellationToken cancellationToken = default) =>
        RunScriptAsync(BluetoothScript, "Bluetooth services and devices re-enabled. Restart if the radio is still missing.", cancellationToken);

    public Task<FixResult> RepairNvidiaAsync(CancellationToken cancellationToken = default) =>
        RunScriptAsync(NvidiaScript, "NVIDIA services restarted and the Control Panel package refreshed.", cancellationToken);

    public Task<FixResult> ResetNetworkAsync(CancellationToken cancellationToken = default) =>
        RunScriptAsync(NetworkScript, "Network adapters enabled and the TCP/IP stack reset. Restart Windows.", cancellationToken);

    public Task<FixResult> EnablePageFileAsync(CancellationToken cancellationToken = default) =>
        RunScriptAsync(PageFileScript, "Automatic managed page file enabled. Restart Windows.", cancellationToken);

    public Task<FixResult> OpenMicrophoneAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessMicrophone");
            registry.DeleteValue("HKCU", @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessMicrophone");
            registry.SetString("HKCU", @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone",
                "Value", "Allow", expand: false);
            environment.OpenShortcut("ms-settings:privacy-microphone");
            return Task.FromResult(FixResult.Success("Microphone policy cleared. Turn on access in the Settings page that opened."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(FixResult.Fail(ex.Message));
        }
    }

    private async Task<FixResult> RunScriptAsync(string script, string success, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(
            PowerShell.Command(script, TimeSpan.FromMinutes(5)), cancellationToken).ConfigureAwait(false);

        if (result.TimedOut)
        {
            return FixResult.Fail("The repair timed out. Restart Windows and try again.");
        }

        if (!result.Ok)
        {
            var detail = result.Combined;
            if (detail.Length > 220)
            {
                detail = detail[..220] + "...";
            }
            return FixResult.Fail(detail.Length == 0 ? "The repair command failed." : detail);
        }

        return FixResult.Success(success);
    }

    private int PersistCurrentDisplayModes()
    {
        var count = 0;
        for (uint index = 0; ; index++)
        {
            var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            if ((device.StateFlags & 0x1) == 0)
            {
                continue;
            }

            var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettings(device.DeviceName, -1, ref mode))
            {
                continue;
            }

            // Passing the current mode back with CDS_UPDATEREGISTRY persists the
            // refresh rate Windows is using instead of hard-coding a monitor mode.
            ChangeDisplaySettingsEx(device.DeviceName, ref mode, IntPtr.Zero, 0x1, IntPtr.Zero);
            count++;
        }

        return count;
    }

    private const string SearchScript = """
        $ErrorActionPreference = 'SilentlyContinue'
        Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search' -Recurse -Force
        Remove-Item -LiteralPath 'HKCU:\SOFTWARE\Policies\Microsoft\Windows\Windows Search' -Recurse -Force
        Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Microsoft\PolicyManager\default\Search' -Recurse -Force
        Set-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' -Name 'SearchboxTaskbarMode' -Value 1 -Type DWord
        Set-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' -Name 'BingSearchEnabled' -Value 0 -Type DWord
        Set-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' -Name 'AllowSearchToUseLocation' -Value 0 -Type DWord
        Set-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows Search' -Name 'SetupCompletedSuccessfully' -Value 0 -Type DWord
        Set-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' -Name 'GlobalUserDisabled' -Value 0 -Type DWord
        Remove-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' -Name 'LetAppsRunInBackground' -Force
        foreach ($exe in @('SearchHost.exe','SearchApp.exe','SearchUI.exe','SearchIndexer.exe','SearchProtocolHost.exe','SearchFilterHost.exe')) {
            Remove-Item -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\' + $exe) -Recurse -Force
        }
        Set-Service -Name WSearch -StartupType Automatic
        Set-Service -Name CDPSvc -StartupType Automatic
        Enable-ScheduledTask -TaskName 'Windows Search' -TaskPath '\Microsoft\Windows\Windows Search\' | Out-Null
        Enable-ScheduledTask -TaskName 'MsCtfMonitor' -TaskPath '\Microsoft\Windows\TextServicesFramework\' | Out-Null
        Stop-Service -Name WSearch -Force
        Get-Process SearchHost,SearchApp,SearchUI,SearchIndexer -ErrorAction SilentlyContinue | Stop-Process -Force
        Remove-Item -LiteralPath "$env:ProgramData\Microsoft\Search\Data\Applications\Windows\Windows.edb" -Force
        Remove-Item -LiteralPath "$env:ProgramData\Microsoft\Search\Data\Applications\Windows\Windows.db" -Force
        Remove-Item -LiteralPath "$env:LOCALAPPDATA\Packages\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\LocalState\Search" -Recurse -Force
        Remove-Item -LiteralPath "$env:LOCALAPPDATA\Packages\Microsoft.Windows.Search_cw5n1h2txyewy\LocalState" -Recurse -Force
        icacls "$env:ProgramData\Microsoft\Search" /reset /T /C /Q | Out-Null
        foreach ($pkg in @('Microsoft.Windows.Search','MicrosoftWindows.Client.CBS','Microsoft.Windows.ShellExperienceHost')) {
            Get-AppxPackage -AllUsers -Name $pkg | ForEach-Object {
                Add-AppxPackage -DisableDevelopmentMode -Register (Join-Path $_.InstallLocation 'AppXManifest.xml') -ErrorAction SilentlyContinue
            }
        }
        Start-Service -Name CDPSvc
        Start-Service -Name WSearch
        Stop-Process -Name explorer -Force
        Start-Process explorer.exe
        exit 0
        """;

    private const string BluetoothScript = """
        $ErrorActionPreference = 'SilentlyContinue'
        $demand = @('bthserv','BthAvctpSvc','BTAGService','BluetoothUserService','DeviceAssociationService','DeviceInstall','DsmSvc','PlugPlay','RmSvc','NcbService','hidserv','SENS','EventSystem','RpcSs')
        foreach ($name in $demand) {
            Set-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $name) -Name Start -Value 3 -Type DWord
            & sc.exe config $name start= demand | Out-Null
        }
        $automatic = @('bthserv','DeviceAssociationService','PlugPlay','RmSvc','RpcSs')
        foreach ($name in $automatic) {
            Set-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $name) -Name Start -Value 2 -Type DWord
            & sc.exe config $name start= auto | Out-Null
        }
        $drivers = @('BTHPORT','BTHUSB','BthEnum','BthLEEnum','BthA2dp','BthHFEnum','BthMini','BthModem','BthPan','BthHFAud','RFCOMM','HidBth','Microsoft_Bluetooth_AvrcpTransport','ibtusb','rtkbtfilter','mtkbtfilter','bcbtums')
        foreach ($name in $drivers) {
            Set-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $name) -Name Start -Value 3 -Type DWord
            & sc.exe config $name start= demand | Out-Null
        }
        Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' -Name AllowBluetooth -Force
        Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Wireless' -Recurse -Force
        Set-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters' -Name SystemRemoteAccessEnabled -Value 1 -Type DWord
        Set-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState' -Value 0 -Type DWord
        Set-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\BluetoothUserService' -Name Type -Value 96 -Type DWord
        Set-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\BluetoothUserService' -Name UserServiceFlags -Value 3 -Type DWord
        $patterns = @('*BTH*','*BLUETOOTH*','*VID_8087*','*VID_0BDA*','*VID_0CF3*','*VID_0A5C*','*VID_0E8D*','*VID_0A12*','*VID_13D3*','*VID_0489*')
        Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
            $dev = $_
            $dev.Class -eq 'Bluetooth' -or $dev.FriendlyName -like '*Bluetooth*' -or ($patterns | Where-Object { $dev.InstanceId -like $_ })
        } | ForEach-Object {
            & pnputil.exe /enable-device $_.InstanceId | Out-Null
            $path = 'HKLM:\SYSTEM\CurrentControlSet\Enum\' + $_.InstanceId + '\Device Parameters'
            if (Test-Path -LiteralPath $path) {
                Set-ItemProperty -LiteralPath $path -Name DeviceSelectiveSuspended -Value 0 -Type DWord
                Set-ItemProperty -LiteralPath $path -Name SelectiveSuspendEnabled -Value 0 -Type DWord
                Set-ItemProperty -LiteralPath $path -Name EnhancedPowerManagementEnabled -Value 0 -Type DWord
            }
        }
        & pnputil.exe /scan-devices | Out-Null
        Get-NetAdapter -IncludeHidden | Where-Object { $_.InterfaceDescription -like '*Bluetooth*' -or $_.Name -like '*Bluetooth*' } | Enable-NetAdapter -Confirm:$false | Out-Null
        foreach ($name in @('PlugPlay','DsmSvc','DeviceInstall','DeviceAssociationService','RmSvc','bthserv','BthAvctpSvc','BTAGService','hidserv')) {
            Start-Service -Name $name
        }
        Get-Service 'BluetoothUserService*' | Set-Service -StartupType Manual
        Get-Service 'BluetoothUserService*' | Start-Service
        exit 0
        """;

    private const string NvidiaScript = """
        $ErrorActionPreference = 'SilentlyContinue'
        $svc = Get-Service -Name 'NVDisplay.ContainerLocalSystem'
        if ($null -eq $svc) {
            $svc = Get-Service | Where-Object { $_.Name -like '*NVDisplay*' -or $_.DisplayName -like '*NVIDIA Display*' } | Select-Object -First 1
        }
        if ($null -ne $svc) {
            Set-Service -Name $svc.Name -StartupType Automatic
            Restart-Service -Name $svc.Name -Force
        }
        if (Get-Command Reset-AppxPackage -ErrorAction SilentlyContinue) {
            Get-AppxPackage *NVIDIACorp.NVIDIAControlPanel* | Reset-AppxPackage
        } else {
            Get-AppxPackage *NVIDIACorp.NVIDIAControlPanel* | ForEach-Object {
                Add-AppxPackage -DisableDevelopmentMode -Register (Join-Path $_.InstallLocation 'AppXManifest.xml') -ErrorAction SilentlyContinue
            }
        }
        $control = "$env:ProgramFiles\NVIDIA Corporation\Control Panel Client\nvcplui.exe"
        if (Test-Path -LiteralPath $control) {
            Start-Process -FilePath $control
        } else {
            $pkg = Get-AppxPackage *NVIDIACorp.NVIDIAControlPanel* | Select-Object -First 1
            if ($null -ne $pkg) {
                $exe = Join-Path $pkg.InstallLocation 'nvcplui.exe'
                if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe }
            }
        }
        exit 0
        """;

    private const string NetworkScript = """
        $ErrorActionPreference = 'SilentlyContinue'
        Get-NetAdapter -IncludeHidden | Enable-NetAdapter -Confirm:$false | Out-Null
        Set-Service -Name WlanSvc -StartupType Automatic
        Start-Service -Name WlanSvc
        netsh wlan set autoconfig enabled=yes interface=* | Out-Null
        netsh winsock reset | Out-Null
        netsh int ip reset | Out-Null
        netsh interface ipv4 reset | Out-Null
        netsh interface ipv6 reset | Out-Null
        ipconfig /flushdns | Out-Null
        netsh advfirewall reset | Out-Null
        Get-Service Dhcp,Dnscache,NlaSvc | Restart-Service -Force
        exit 0
        """;

    private const string PageFileScript = """
        $ErrorActionPreference = 'Stop'
        try {
            $computer = Get-CimInstance -ClassName Win32_ComputerSystem
            if (-not $computer.AutomaticManagedPagefile) {
                Set-CimInstance -InputObject $computer -Property @{ AutomaticManagedPagefile = $true }
            }
            exit 0
        } catch {
            Write-Error $_
            exit 1
        }
        """;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DevMode devMode, IntPtr hwnd, uint flags, IntPtr lParam);
}
