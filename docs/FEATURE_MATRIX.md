# Feature matrix — 0.2.5

The legacy build is the 0.2.0 Electron repository. “Parity” below means the feature exists with the
same visible behaviour; “improved” means the rewrite fixes a defect or removes a shell
dependency. Every free tweak is present; the PRO catalog is present but gated.

## Navigation

| Page | Legacy | 0.2.5 | Notes |
| --- | --- | --- | --- |
| Dashboard | Metrics only | Metrics + system-information cards | Native CPU/memory/disk/GPU/uptime, no WMI |
| Background Monitor | 40 processes, kill, block | Same | Native process API; IFEO block with previous Debugger backup |
| Shortcuts | 9 categories / 85 tools | Same | ShellExecute, no console |
| Windows | 79 tweaks | 79 | Native registry + service actions |
| Gaming | 15 tweaks | 15 | Includes BCDEdit process actions |
| GPU | 27 tweaks | 27 | Native |
| Power | 20 tweaks | 20 | Native powercfg |
| Network | 12 tweaks | 12 | Native |
| Privacy | 23 tweaks | 23 | Native |
| Context Menu | 21 entries, 3 inert | 18 working entries | Three fabricated CLSIDs removed (see fixes) |
| Brave Debloater | 19 tweaks | 19 | Native |
| Windows Debloat | AppX groups | 37 groups | PowerShell only for `Get-AppxPackage` / removal |
| Cleaner | 9 categories | 9 | Native file deletion + recycle-bin API; no PowerShell |
| Power Plans | Studio + 20 plans + timer | Installed schemes, active badge, all 20 ready-made plans | Settings/editor removed by request; native powercfg; duplicate-per-apply fixed |
| Startup Manager | 4 Run keys + 2 folders | Same | Native StartupApproved blobs / file renames |
| Personalize | 21 tweaks | 21 | Accent writes are native, no PowerShell helper |
| Advanced Power | PRO | PRO gate | Catalog + service layer present; locked |
| Windows Update | PRO | PRO gate | 19 update tweaks embedded in the PRO catalog |
| DNS Optimizer | PRO | PRO gate | Locked like the legacy build |
| Driver Hub | PRO | PRO gate | Locked like the legacy build |
| Get Atom Optimizer | PRO modal | PRO page | Clear upgrade boundary |
| Backup & Restore | Restore points + .reg | Same | `SRSetRestorePoint`, `reg export/import` |
| App Installer | 51 apps | 51 | HttpClient download with progress/cancel |
| Installed Apps | Registry + AppX | Same | Native desktop enumeration, one AppX pass |
| History | 200 entries, revert all | Same | JSON persistence, revert via catalog/undo token |
| Settings | Theme, smooth scroll, reset, audit | Same | Three themes, keyboard-accessible controls |

## Tweak engine

| Capability | Legacy | 0.2.5 |
| --- | --- | --- |
| Storage | Batch files + `reg.exe` | Typed catalog + registry API |
| Verification | PowerShell per tweak | Native read-back for registry tweaks; legacy checks on demand |
| Batch checks | Wrapped many checks in one PowerShell | 16-way parallel native reads |
| Service changes | `sc.exe` | `advapi32` service APIs |
| Accent | PowerShell helper + Explorer restart | Native palette writer + broadcast |
| Power plan apply | `powercfg /import` every click | GUID cache, idempotent activate |
| Cleaner | PowerShell per category | Native deletion; locked files skipped |
| State | localStorage | Atomic JSON under LocalAppData |
| Errors | String rejections | Typed `OperationResult`, partial-failure collection |

## Catalog coverage

| Metric | Value |
| --- | --- |
| Catalog pages | 10 (8 free, 2 PRO) |
| Total tweaks | 295 |
| Free tweaks | 216 |
| Registry actions | 564 |
| Registry-delete actions | 56 |
| Service actions | 68 |
| powercfg actions | 89 |
| Process actions | 29 |
| PowerShell actions | 22 |
| Accent actions | 20 |
| Batch fallbacks | 10 (all PRO) |

Full per-tweak reasons are in `tools/catalog-report.txt`.

## Deliberate fixes and improvements

1. **Power Plans is now a focused switcher.** The settings editor, timer controls and plan CRUD were removed; the complete legacy set of 20 ready-made plans remains, one click each, with no duplicate imports.
2. **Power-plan duplicate storm.** The legacy APPLY button imported the bundled `.pow` on
   every click, creating a new plan each time. 0.2.5 caches the imported GUID and activates the
   existing plan; it only imports when the plan is genuinely missing.
3. **No PowerShell for the free engine.** Accent, startup, context-menu and cleaner operations
   are native. Only the 10 PRO fallbacks still use batch/PowerShell.
4. **Verification is real.** Registry changes are read back value-by-value instead of trusting a
   batch exit code. Failures keep the toggle in its previous state.
5. **Three inert context-menu GUIDs removed.** `Include in library`, `Edit with Paint` and
   `Set as desktop` used fabricated CLSIDs from the legacy list and did nothing. The remaining
   18 entries are backed by real shell keys.
6. **Scroll position resets between pages.** A reused WPF view previously kept the previous
   page's offset; the new `ScrollReset` behaviour restores the top on navigation.
7. **No shared resource-name collision.** The admin-warning border style was renamed
   `AdminWarning` so it cannot shadow the `Warning` brush used for text.
8. **Power-plan GUID parsing fixed.** The legacy-compatible regex now names the GUID group;
   plan selection, ACTIVE badges and detail loading work.
9. **Keyboard-accessible toggles.** Toggle switches are focusable and expose automation names.
10. **Single instance activation.** A second launch signals the existing window instead of
   duplicating the process.
11. **Read-only bindings.** Progress bars bind one-way to computed percentages; WPF no longer
    raises binding exceptions during layout.

## Known limitations

- The four ADVANCED modules (Advanced Power, Windows Update, DNS, Driver Hub) remain PRO, as in
  the legacy free build. Their catalog data and service seams are present.
- The 10 PRO batch fallbacks still execute legacy script text (Windows Update policies,
  OneDrive/temp removal). They are not reachable without a PRO licence.
- Restore points require System Protection to be enabled for the system drive.
- AppX enumeration/removal uses PowerShell because Windows exposes no practical managed API
  without additional COM/WinRT plumbing.
- Vendor EXE installers remain interactive; MSI installs run silently to completion.
- Obfuscation and Authenticode signing are release steps, not applied in this repository.
