using System.Windows;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.ViewModels.Items;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class FixesViewModel : PageViewModel
{
    private readonly ISystemFixService _fixes;

    public FixesViewModel(ISystemFixService fixes, IEnvironmentService environment, Action<string, string?> toast)
    {
        _fixes = fixes;
        Title = "FIXES & REPAIRS";
        CountText = "(18)";
        Intro = "Advanced repairs for custom Windows builds and broken system components. Read each card before running it; some repairs restart Explorer, reset networking, remove cached data or require a reboot.";
        ShowAdminWarning = true;
        HasConsole = false;

        Sections.Add(new PageSectionViewModel
        {
            IsCard = true,
            Title = "DISPLAY & GRAPHICS",
            Items =
            [
                new ActionRowViewModel(
                    "DISPLAY REFRESH RESET",
                    "Clears stale monitor configuration, re-applies the current refresh rate for every active display, restores the display broker and raises the TDR delay. Use this when Windows keeps falling back to 60 Hz.",
                    "RUN FIX",
                    () => ConfirmThen("DISPLAY REFRESH RESET",
                        "This rewrites the current display mode for all monitors and restarts the display broker.\n\nContinue?",
                        _fixes.RepairDisplayAsync)),
                new ActionRowViewModel(
                    "NVIDIA CONTROL PANEL",
                    "Restarts NVIDIA display services and re-registers the NVIDIA Control Panel package. Useful when the panel does not open or its Store package is broken.",
                    "RUN FIX",
                    () => ConfirmThen("NVIDIA CONTROL PANEL",
                        "This restarts NVIDIA services and refreshes the Control Panel package.\n\nContinue?",
                        _fixes.RepairNvidiaAsync))
            ]
        });

        Sections.Add(new PageSectionViewModel
        {
            IsCard = true,
            Title = "CORE WINDOWS",
            Items =
            [
                new ActionRowViewModel(
                    "WINDOWS UPDATE",
                    "Stops the update stack, removes the SoftwareDistribution and catroot2 caches, clears update policy blocks and starts wuauserv, BITS and cryptographic services again.",
                    "RESET UPDATE",
                    () => ConfirmThen("WINDOWS UPDATE",
                        "This clears the downloaded update cache and restarts Windows Update services.\n\nContinue?",
                        _fixes.RepairWindowsUpdateAsync)),
                new ActionRowViewModel(
                    "COMPONENT STORE / SFC",
                    "Runs DISM RestoreHealth followed by System File Checker. Use this for corrupted system files, failed updates or a damaged component store. This can take 15–30 minutes.",
                    "RUN REPAIR",
                    () => ConfirmThen("COMPONENT STORE / SFC",
                        "This can take 15–30 minutes and uses significant disk and CPU time.\n\nContinue?",
                        _fixes.RepairComponentStoreAsync)),
                new ActionRowViewModel(
                    "WMI REPOSITORY",
                    "Verifies the WMI repository, attempts a salvage if it is inconsistent and restarts the Windows Management Instrumentation service. Fixes many broken Settings and installer errors.",
                    "RUN FIX",
                    () => ConfirmThen("WMI REPOSITORY",
                        "This checks and may salvage the WMI repository, then restarts the WMI service.\n\nContinue?",
                        _fixes.RepairWmiAsync)),
                new ActionRowViewModel(
                    "WINDOWS SECURITY",
                    "Starts Defender, Security Center and related services, removes anti-malware policy blocks, re-enables real-time monitoring and re-registers the Security app.",
                    "RUN FIX",
                    () => ConfirmThen("WINDOWS SECURITY",
                        "This re-enables Windows Security services and real-time protection where policy allows.\n\nContinue?",
                        _fixes.RepairSecurityAsync))
            ]
        });

        Sections.Add(new PageSectionViewModel
        {
            IsCard = true,
            Title = "WINDOWS & DEVICES",
            Items =
            [
                new ActionRowViewModel(
                    "WINDOWS SEARCH",
                    "Removes search policy blocks, rebuilds the index store, repairs the Search packages and restarts Windows Search and Explorer.",
                    "RUN FIX",
                    () => ConfirmThen("WINDOWS SEARCH",
                        "This deletes the search index and restarts Explorer and Windows Search.\nThe index will rebuild in the background.\n\nContinue?",
                        _fixes.RepairSearchAsync)),
                new ActionRowViewModel(
                    "BLUETOOTH",
                    "Restores Bluetooth services and drivers, clears radio policy blocks and re-enables Bluetooth devices. Restart if the adapter or radio is still missing.",
                    "RUN FIX",
                    () => ConfirmThen("BLUETOOTH",
                        "This restarts Bluetooth services and re-enables matching devices.\n\nContinue?",
                        _fixes.RepairBluetoothAsync)),
                new ActionRowViewModel(
                    "MICROPHONE ACCESS",
                    "Clears microphone policy blocks and opens the Windows privacy page. Turn on Microphone Access and app access there. No reboot is needed.",
                    "OPEN SETTINGS",
                    async () =>
                    {
                        var result = await _fixes.OpenMicrophoneAsync().ConfigureAwait(true);
                        return new ActionOutcome(result.Ok, result.Message);
                    }),
                new ActionRowViewModel(
                    "DEVICE RESCAN",
                    "Rescans Plug and Play, re-enables every device reporting an error and starts the device installation services. Useful after driver cleanup or a stripped custom image.",
                    "RESCAN",
                    () => ConfirmThen("DEVICE RESCAN",
                        "This rescans all devices and re-enables devices marked with an error.\n\nContinue?",
                        _fixes.RepairDevicesAsync)),
                new ActionRowViewModel(
                    "TIME SYNC",
                    "Restarts Windows Time, points it at time.windows.com and forces a clock resynchronization. Fixes broken certificates, sign-ins and update checks caused by clock drift.",
                    "SYNC",
                    () => ConfirmThen("TIME SYNC",
                        "This changes the time source to time.windows.com and resynchronizes the clock.\n\nContinue?",
                        _fixes.RepairTimeAsync))
            ]
        });

        Sections.Add(new PageSectionViewModel
        {
            IsCard = true,
            Title = "APPS, SHELL & MEDIA",
            Items =
            [
                new ActionRowViewModel(
                    "MICROSOFT STORE",
                    "Starts the Store and AppX services, re-registers the Store package and clears its cache. Fixes Store pages that fail to load or apps that will not install.",
                    "RUN FIX",
                    () => ConfirmThen("MICROSOFT STORE",
                        "This refreshes the Store package and clears its cache. The Store may close briefly.\n\nContinue?",
                        _fixes.RepairStoreAsync)),
                new ActionRowViewModel(
                    "SHELL & ICON CACHE",
                    "Restarts Explorer, rebuilds icon and thumbnail caches, clears the font cache and re-registers the shell and Start menu packages. Fixes blank icons and a frozen taskbar.",
                    "REBUILD",
                    () => ConfirmThen("SHELL & ICON CACHE",
                        "Explorer will restart and the desktop will flash briefly.\n\nContinue?",
                        _fixes.RepairShellAsync)),
                new ActionRowViewModel(
                    "AUDIO SERVICES",
                    "Starts AudioEndpointBuilder and Audiosrv, re-enables audio devices in error and rescans for missing sound hardware. Fixes no-output and missing-device problems.",
                    "RUN FIX",
                    () => ConfirmThen("AUDIO SERVICES",
                        "This restarts the Windows audio stack and re-enables audio devices.\n\nContinue?",
                        _fixes.RepairAudioAsync)),
                new ActionRowViewModel(
                    "PRINT SPOOLER",
                    "Starts the Print Spooler, clears stuck print jobs, re-enables printer devices and rescans the device tree. Fixes printers that disappear or jobs that never leave the queue.",
                    "RUN FIX",
                    () => ConfirmThen("PRINT SPOOLER",
                        "This restarts the spooler and deletes all queued print jobs.\n\nContinue?",
                        _fixes.RepairPrintAsync)),
                new ActionRowViewModel(
                    "XBOX & GAMING SERVICES",
                    "Restores Xbox authentication, save, networking and Gaming Services packages. Use this when the Xbox app, Game Pass or game sign-in is broken on a custom build.",
                    "RUN FIX",
                    () => ConfirmThen("XBOX & GAMING SERVICES",
                        "This starts Xbox services and refreshes the Gaming Services package.\n\nContinue?",
                        _fixes.RepairGamingAsync))
            ]
        });

        Sections.Add(new PageSectionViewModel
        {
            IsCard = true,
            Title = "NETWORK & STORAGE",
            Items =
            [
                new ActionRowViewModel(
                    "NETWORK & WI-FI RESET",
                    "Enables every network adapter, turns on WLAN AutoConfig, resets Winsock, TCP/IP and the firewall, then flushes DNS. Restart Windows afterwards.",
                    "RUN FIX",
                    () => ConfirmThen("NETWORK & WI-FI RESET",
                        "This resets the network stack and firewall rules and will briefly disconnect all connections.\n\nRestart Windows afterwards.\n\nContinue?",
                        _fixes.ResetNetworkAsync)),
                new ActionRowViewModel(
                    "PAGE FILE",
                    "Enables the automatic managed page file. This can stop paging-related crashes and out-of-memory failures after the next restart.",
                    "ENABLE",
                    () => ConfirmThen("PAGE FILE",
                        "This enables the automatic managed page file.\nRestart Windows to apply it.\n\nContinue?",
                        _fixes.EnablePageFileAsync))
            ]
        });

        _ = environment;
        _ = toast;
    }

    private static async Task<ActionOutcome> ConfirmThen(string title, string message, Func<CancellationToken, Task<FixResult>> action)
    {
        var confirmation = MessageBox.Show(message, "Atom ToolBox - " + title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return new ActionOutcome(false, "CANCELLED");
        }

        try
        {
            var result = await action(CancellationToken.None).ConfigureAwait(true);
            return new ActionOutcome(result.Ok, result.Message);
        }
        catch (Exception ex)
        {
            return new ActionOutcome(false, ex.Message);
        }
    }
}
