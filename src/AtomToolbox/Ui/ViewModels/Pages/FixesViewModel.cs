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
        CountText = "(7)";
        Intro = "Targeted repairs for common Windows breakages. Read each card before running it; some repairs restart Explorer, reset networking or require a reboot.";
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
                    })
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
