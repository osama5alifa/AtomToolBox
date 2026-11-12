using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Infrastructure.Registry;

namespace Atom.Toolbox.Infrastructure.ContextMenu;

/// <summary>
/// All context-menu entries are written directly to HKCU. The five "add" verbs use the
/// same keys and command lines as the legacy helper, but no PowerShell process is spawned.
/// </summary>
public sealed class ContextMenuService(RegistryStore registry) : IContextMenuService
{
    private const string BlockedKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private const string CopyPathCommand =
        "powershell.exe -NoProfile -WindowStyle Hidden -Command \"Set-Clipboard -Value '%1'\"";

    private const string CmdHereCommand = "cmd.exe /s /k pushd \"%V\"";
    private const string PowerShellHereCommand =
        "powershell.exe -NoProfile -NoExit -Command \"Set-Location -LiteralPath '%V'\"";

    private const string NotepadCommand = "notepad.exe \"%1\"";

    private const string TakeOwnershipCommand =
        "powershell.exe -NoProfile -WindowStyle Hidden -Command \"Start-Process cmd -Verb RunAs -ArgumentList '/c takeown /f \\\"%1\\\" /r /d y && icacls \\\"%1\\\" /grant *S-1-5-32-544:F /t'\"";

    public IReadOnlyList<ContextMenuDefinition> Entries { get; } = BuildEntries();

    public Task<IReadOnlyDictionary<string, bool>> GetStateAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyDictionary<string, bool>>(() =>
            Entries.ToDictionary(e => e.Id, IsEnabled, StringComparer.OrdinalIgnoreCase), cancellationToken);

    public Task<OperationResult> SetAsync(ContextMenuDefinition definition, bool enabled, CancellationToken cancellationToken = default) =>
        Task.Run(() => Set(definition, enabled), cancellationToken);

    private OperationResult Set(ContextMenuDefinition definition, bool enabled) =>
        definition.Kind switch
        {
            ContextMenuKind.Block => SetBlock(definition, enabled),
            ContextMenuKind.Key => SetKey(definition, enabled),
            ContextMenuKind.DWord => SetDWord(definition, enabled),
            ContextMenuKind.AddVerb => SetVerb(definition, enabled),
            _ => OperationResult.Fail("Unsupported context-menu entry.")
        };

    private OperationResult SetBlock(ContextMenuDefinition definition, bool enabled)
    {
        if (enabled)
        {
            registry.SetString("HKLM", BlockedKey, definition.ValueName!, string.Empty, expand: false);
        }
        else
        {
            registry.DeleteValue("HKLM", BlockedKey, definition.ValueName!);
        }

        var present = registry.ValueExists("HKLM", BlockedKey, definition.ValueName!);
        return present == enabled
            ? OperationResult.Success
            : OperationResult.Fail("The shell extension block list could not be verified.");
    }

    private OperationResult SetKey(ContextMenuDefinition definition, bool enabled)
    {
        if (enabled)
        {
            registry.EnsureKey("HKCU", definition.Key!);
        }
        else
        {
            registry.DeleteKeyTree("HKCU", definition.RemoveKey ?? definition.Key!);
        }

        var exists = registry.KeyExists("HKCU", definition.Key!);
        return exists == enabled
            ? OperationResult.Success
            : OperationResult.Fail("The context-menu key could not be verified.");
    }

    private OperationResult SetDWord(ContextMenuDefinition definition, bool enabled)
    {
        if (enabled)
        {
            registry.SetDWord("HKCU", definition.Key!, definition.ValueName!, 1);
        }
        else
        {
            registry.DeleteValue("HKCU", definition.Key!, definition.ValueName!);
        }

        var present = registry.ValueExists("HKCU", definition.Key!, definition.ValueName!);
        return present == enabled
            ? OperationResult.Success
            : OperationResult.Fail("The context-menu value could not be verified.");
    }

    private OperationResult SetVerb(ContextMenuDefinition definition, bool enabled)
    {
        if (!enabled)
        {
            foreach (var key in definition.VerbKeys)
            {
                registry.DeleteKeyTree("HKCU", key.Key);
            }

            var remaining = definition.VerbKeys.Where(k => registry.KeyExists("HKCU", k.Key)).ToArray();
            return remaining.Length == 0
                ? OperationResult.Success
                : OperationResult.Fail($"Could not remove {remaining.Length} context-menu key(s).");
        }

        foreach (var key in definition.VerbKeys)
        {
            registry.EnsureKey("HKCU", key.Key);
            foreach (var (name, value) in key.Values)
            {
                registry.SetString("HKCU", key.Key, name, value, expand: false);
            }
            registry.SetString("HKCU", key.Key + @"\command", string.Empty, key.Command, expand: false);
        }

        foreach (var key in definition.VerbKeys)
        {
            if (!registry.TryRead("HKCU", key.Key + @"\command", string.Empty, out var command) ||
                !string.Equals(command.AsText(), key.Command, StringComparison.Ordinal))
            {
                return OperationResult.Fail("The new context-menu command could not be verified.");
            }
        }

        return OperationResult.Success;
    }

    private bool IsEnabled(ContextMenuDefinition definition) => definition.Kind switch
    {
        ContextMenuKind.Block => registry.ValueExists("HKLM", BlockedKey, definition.ValueName!),
        ContextMenuKind.Key => registry.KeyExists("HKCU", definition.Key!),
        ContextMenuKind.DWord => registry.ValueExists("HKCU", definition.Key!, definition.ValueName!),
        ContextMenuKind.AddVerb => definition.VerbKeys.All(k => registry.KeyExists("HKCU", k.Key)),
        _ => false
    };

    private static IReadOnlyList<ContextMenuDefinition> BuildEntries() =>
    [
        new()
        {
            Id = "show_more_options",
            Name = "RESTORE OLD CONTEXT MENU",
            Description = "Bypasses the Windows 11 condensed menu and restores the classic right-click menu.",
            Kind = ContextMenuKind.Key,
            Key = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32",
            RemoveKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}"
        },
        Block("give_access_to", "REMOVE \"GIVE ACCESS TO\"", "Removes \"Give access to\" from the context menu.", "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}"),
        Block("share", "REMOVE \"SHARE\"", "Removes the Share option from the context menu.", "{e2bf9676-5f8f-435c-9253-8a2b481b5f52}"),
        Block("cast_to_device", "REMOVE \"CAST TO DEVICE\"", "Removes \"Cast to Device\" from the context menu.", "{7AD84985-87B4-4a16-BE58-8B72A5B390F7}"),
        Block("scan_defender", "REMOVE \"SCAN WITH DEFENDER\"", "Removes \"Scan with Microsoft Defender\" from the context menu.", "{09A47860-11B0-4DA5-AFA5-26D86198A780}"),
        Block("pin_start", "REMOVE \"PIN TO START\"", "Removes \"Pin to Start\" from the context menu.", "{90AA3A4E-1CBA-4233-B8BB-535773D48449}"),
        Block("pin_taskbar", "REMOVE \"PIN TO TASKBAR\"", "Removes \"Pin to taskbar\" from the context menu.", "{538C0999-6A52-4C66-8E61-8D5F7B2A0F6A}"),
        Block("previous_versions", "REMOVE \"PREVIOUS VERSIONS\"", "Removes \"Previous Versions\" from the context menu.", "{596AB062-B4D2-4215-9F74-E9109B0A8153}"),
        Block("print", "REMOVE \"PRINT\"", "Removes Print from the context menu.", "{b1eb6d45-4f1b-11ce-abe1-00aa00b9e6ba}"),
        Block("send_to", "REMOVE \"SEND TO\"", "Removes \"Send to\" from the context menu.", "{7BA4C740-9E81-11CF-99D3-00AA004AE837}"),
        Block("copy_to_folder", "REMOVE \"COPY TO FOLDER\"", "Removes \"Copy to folder\" from the context menu.", "{C2FBB630-2971-11d1-A18C-00C04FD75D13}"),
        Block("move_to_folder", "REMOVE \"MOVE TO FOLDER\"", "Removes \"Move to folder\" from the context menu.", "{C2FBB631-2971-11d1-A18C-00C04FD75D13}"),
        new()
        {
            Id = "new_bing_search",
            Name = "REMOVE \"BING SEARCH\"",
            Description = "Removes Bing search from the right-click menu on selected text.",
            Kind = ContextMenuKind.DWord,
            Key = ExplorerAdvanced,
            ValueName = "DisableSearchBoxSuggestions"
        },
        Verb("copy_path", "ADD \"COPY AS PATH\"",
            "Adds a working \"Copy as path\" entry to files and folders (HKCU, no admin needed).",
            new ContextMenuVerbKey(
                @"Software\Classes\AllFilesystemObjects\shell\AtomCopyPath",
                new Dictionary<string, string>
                {
                    ["MUIVerb"] = "Copy as path",
                    ["Icon"] = "imageres.dll,-5302",
                    ["Position"] = "Top"
                },
                CopyPathCommand)),
        Verb("cmd_here", "ADD \"OPEN CMD HERE\"",
            "Adds \"Open Command Prompt here\" to folder and folder-background menus.",
            new ContextMenuVerbKey(
                @"Software\Classes\Directory\shell\AtomCmdHere",
                new Dictionary<string, string> { ["MUIVerb"] = "Open Command Prompt here", ["Icon"] = "cmd.exe" },
                CmdHereCommand),
            new ContextMenuVerbKey(
                @"Software\Classes\Directory\Background\shell\AtomCmdHere",
                new Dictionary<string, string> { ["MUIVerb"] = "Open Command Prompt here", ["Icon"] = "cmd.exe" },
                CmdHereCommand)),
        Verb("powershell_here", "ADD \"OPEN POWERSHELL HERE\"",
            "Adds \"Open PowerShell here\" to folder and folder-background menus.",
            new ContextMenuVerbKey(
                @"Software\Classes\Directory\shell\AtomPwshHere",
                new Dictionary<string, string> { ["MUIVerb"] = "Open PowerShell here", ["Icon"] = "powershell.exe" },
                PowerShellHereCommand),
            new ContextMenuVerbKey(
                @"Software\Classes\Directory\Background\shell\AtomPwshHere",
                new Dictionary<string, string> { ["MUIVerb"] = "Open PowerShell here", ["Icon"] = "powershell.exe" },
                PowerShellHereCommand)),
        Verb("notepad", "ADD \"OPEN WITH NOTEPAD\"",
            "Adds \"Open with Notepad\" to the right-click menu of every file.",
            new ContextMenuVerbKey(
                @"Software\Classes\*\shell\AtomOpenNotepad",
                new Dictionary<string, string> { ["MUIVerb"] = "Open with Notepad", ["Icon"] = "notepad.exe" },
                NotepadCommand)),
        Verb("take_ownership", "ADD \"TAKE OWNERSHIP\"",
            "Adds a working Take Ownership entry (UAC prompt on click; grants Administrators full control).",
            new ContextMenuVerbKey(
                @"Software\Classes\*\shell\TakeOwnership",
                new Dictionary<string, string>
                {
                    ["MUIVerb"] = "Take Ownership",
                    ["Icon"] = "takeown.exe",
                    ["HasLUAShield"] = ""
                },
                TakeOwnershipCommand),
            new ContextMenuVerbKey(
                @"Software\Classes\Directory\shell\TakeOwnership",
                new Dictionary<string, string>
                {
                    ["MUIVerb"] = "Take Ownership",
                    ["Icon"] = "takeown.exe",
                    ["HasLUAShield"] = ""
                },
                TakeOwnershipCommand))
    ];

    private static ContextMenuDefinition Block(string id, string name, string description, string clsid) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        Kind = ContextMenuKind.Block,
        Key = BlockedKey,
        ValueName = clsid,
        WritesMachineState = true
    };

    private static ContextMenuDefinition Verb(string id, string name, string description, params ContextMenuVerbKey[] keys) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        Kind = ContextMenuKind.AddVerb,
        VerbKeys = keys
    };
}
