# QA and verification

## Automated engine self-test

```powershell
dotnet src/AtomToolbox/bin/Release/net8.0-windows/AtomToolbox.dll --self-test
```

On the reference machine:

```text
[PASS] catalog - 295 tweaks / 10 pages
[PASS] free pages - 216 free tweaks
[PASS] bundled power plans - 20 plans
[PASS] shortcut catalog - 85 shortcuts / 9 categories
[SKIP] registry write - this environment blocks HKCU writes.
[PASS] powercfg list - 4 plans
[PASS] powercfg read scheme - 140 settings for Atom-OS Ultimate Performance
[PASS] startup scan - 5 entries
[PASS] context menu state - 18 entries
RESULT: PASS
```

The registry write is skipped only when the process token cannot write HKCU (for example under
a build sandbox). Run the same command elevated on a normal desktop to exercise the apply /
verify / revert path.

## UI smoke test

The release checklist drives WPF through UI Automation:

1. Launch the manifest-free test build with `ATOM_NO_ELEVATION=1` and `ATOM_DATA_ROOT` on a
   disposable path.
2. Invoke every sidebar button by its `AutomationProperties.Name`.
3. Assert zero `dispatcher:` entries in `app.log`.

Result for 0.2.5: **21 pages opened (initial dashboard + 20 navigation invocations), zero dispatcher exceptions**. Pages exercised:
Dashboard, Background Monitor, Shortcuts, Windows, Gaming, GPU, Power, Network, Privacy,
Context Menu, Brave Debloater, Cleaner, Power Plans, Startup Manager, Personalize,
Backup & Restore, App Installer, History, Settings, Windows Debloat, Installed Apps.

Additional checks performed:

- Dark → Light → Dark theme switch: content repainted to `#F4F5F7` and back.
- Power Plans: loaded 4 installed schemes and all 20 ready-made legacy plans; the active badge
  matched the active scheme. The page no longer exposes settings/timer/editor controls.
- Startup Manager: 5 entries, 3 enabled, 2 stale overrides noted.
- Windows page: 79 tweaks rendered with native check state and toggle controls.
- Scroll position resets on navigation (no page opens mid-document).

## Performance snapshot

Measured on the reference machine (Windows 10 19045, 4 logical cores, 16 GB RAM), warm launch,
Release configuration, framework-dependent:

| Run | Window visible |
| --- | --- |
| 1 | 777 ms |
| 2 | 773 ms |
| 3 | 760 ms |
| 4 | 775 ms |

Working set after the dashboard settled: **~126 MB**. The legacy Electron process tree was
multiple processes over several hundred MB. This figure includes WPF, the parsed 295-tweak
catalog, the theme dictionary and the rendered dashboard.

Most of the startup budget is WPF/CLR initialization and BAML loading, not application work
(catalog construction is ~40 ms, theme loading ~45 ms). ReadyToRun publishing would reduce JIT
time further but requires the crossgen2 toolchain, which is not in the offline SDK cache on the
reference machine.

## Installer QA

The IExpress package was validated by extracting its 245-file payload, running `setup.ps1` in
quiet mode against a temporary install directory, and running `uninstall.ps1` against the same
directory. Result: 243 files installed (plus the uninstaller), app present, and the directory
removed cleanly. Shortcut and HKLM registration paths were reviewed but require an interactive
elevated desktop to exercise fully.

## Manual checklist

- [ ] Toggle a HKCU tweak on/off and confirm the registry reads back.
- [ ] Toggle a service-backed tweak and confirm the service start mode.
- [ ] `CHECK STATE` on the Windows page reports the correct active count.
- [ ] Apply a ready-made power plan; confirm it is imported once, activated, and shown active.
- [ ] Switch between two installed schemes and confirm the active badge follows.
- [ ] Disable and re-enable a startup entry; confirm the StartupApproved blob.
- [ ] Add and remove a context-menu verb; confirm the key.
- [ ] Create a restore point (System Protection enabled).
- [ ] Export and re-import a registry backup.
- [ ] Install a small MSI; verify progress and cancel on a second app.
- [ ] Run the Cleaner on one category.
- [ ] Export the registry audit and open the report.
- [ ] Switch themes and restart the app; the choice persists.
- [ ] Run `AtomToolbox-Setup-0.2.5.exe` on a clean VM; confirm UAC, shortcuts, Apps & Features entry and uninstall.
