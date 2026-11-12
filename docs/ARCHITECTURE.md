# Architecture

## Goals

1. Native cold start and a small steady-state working set.
2. Every legacy tweak represented as reviewed, replayable data rather than shell strings.
3. A UI that can be changed without touching Windows logic, and vice versa.
4. A clear, enforceable premium boundary.

## Layers

```text
Desktop (WPF) ──► Application ──► Domain
      │                │
      └────────► Infrastructure ──► Domain/Application ports
```

- **Domain** has no Windows or UI knowledge: catalog entities, power models, maintenance
  records, history entries.
- **Application** owns use cases and ports. `TweakService` coordinates execute → verify →
  persist → audit. `HistoryService` knows how to revert a change; it does not know how a
  registry write happens.
- **Infrastructure** implements the ports with Win32, the registry, `powercfg`, AppX and
  PowerShell (only where Windows has no practical native API).
- **Desktop** is the composition root and WPF presentation. It depends on abstractions and
  constructs concrete services in `Program.cs`.

All projects are compiled into one assembly on purpose. The physical folders preserve the
layer boundaries; the single assembly removes cross-assembly startup work and lets the release
be a single signed executable. The earlier multi-project layout was abandoned because the
target machine's SDK cannot resolve multi-project MSBuild graphs.

## Catalog pipeline

`tools/Atom.CatalogBuilder` is a development-time compiler:

1. Reads the legacy free `src/config/*.json` files (216 tweaks across eight pages).
2. Reads the sibling `.bat` scripts.
3. Parses `reg add/delete`, `sc`, `powercfg`, `netsh`, `bcdedit`, `powershell` and simple
   process invocations into `TweakAction` records.
4. Falls back to a single embedded batch action when control flow or a helper file makes a
   faithful translation unsafe.
5. Emits `tweaks.json` plus a SHA-256 sidecar, and copies the 20 `.pow` plans and the helper
   scripts into the assembly resources.

The generated file is checked in. `tools/catalog-report.txt` records coverage by action kind
and lists every fallback with its reason. At runtime the catalog is validated (known action
kinds, unique ids, page shape) and the SHA-256 is verified at startup. The builder preserves
hand-maintained entries already present in `tweaks.json`; the committed catalog currently adds
the 12 promoted and 6 WinUtil-parity tweaks on top of the 216 legacy free entries.

## Execution and verification

`TweakExecutor` runs actions sequentially, collecting errors rather than aborting on the first
one. A registry-only tweak verifies by reading back every value it wrote; this is both stricter
and hundreds of times faster than spawning PowerShell per tweak. Tweaks that depend on a
service, `powercfg` or a boot configuration verify with the legacy check command, and those
checks only run on demand (`CHECK STATE`), keeping page loads instant.

`TweakService.VerifyPageAsync` treats the two classes differently: native checks run with a
parallelism of 16, shell checks with a parallelism of 2. On a normal machine a full 90-tweak
page scan completes in a few hundred milliseconds.

## Threading

- Every command that can block is async and awaits `Process.WaitForExitAsync`.
- The UI thread only performs element updates. PowerShell, downloads, scans and file cleanup
  run off-thread and report through `IProgress<T>`.
- The CPU probe's first sample intentionally waits 120 ms so the first dashboard frame shows a
  real percentage instead of `0%`.
- No global polling timers exist outside the 30-second header refresh and the opt-in 5-second
  monitor refresh, which stops when the page is left.

## State

| File | Location | Contents |
| --- | --- | --- |
| `settings.json` | `%LOCALAPPDATA%\AtomToolBox` | theme, smooth scrolling |
| `tweak-state.json` | `%LOCALAPPDATA%\AtomToolBox` | last known toggle states |
| `history.json` | `%LOCALAPPDATA%\AtomToolBox` | 200-entry undo history |
| `blocked.json` | `%APPDATA%\AtomToolBox` | IFEO blocks with the previous Debugger value |
| `power-schemes.json` | `%LOCALAPPDATA%\AtomToolBox` | bundled-plan/template → GUID cache |
| `backups/*.reg` | `%APPDATA%\AtomToolBox\backups` | registry exports |
| `logs/app.log` | `%LOCALAPPDATA%\AtomToolBox\logs` | unhandled errors and command failures |

Writes are atomic (`.tmp` + move) and failures are swallowed: state persistence must never take
the UI down. `ATOM_DATA_ROOT` relocates all per-user data for portable use.

## Win32 usage

- `GetSystemTimes`, `GlobalMemoryStatusEx`, `GetTickCount64` — CPU, memory, uptime.
- `EnumDisplayDevices` — GPU model, no WMI.
- `advapi32` service APIs — start mode and control.
- `ntdll!NtSetTimerResolution` — process-held fast timer, released automatically on exit.
- `srclient!SRSetRestorePoint` — restore points.
- `shell32!SHEmptyRecycleBin` — recycle bin.
- `user32!SendMessageTimeout(WM_SETTINGCHANGE, "ImmersiveColorSet")` — accent refresh.

No WMI queries and no performance-counter libraries are used, which keeps startup and memory
predictable.

## UI composition

The shell is a custom-chrome WPF window: fixed 56 px title strip, 210 px grouped sidebar,
content area, 26 px status strip. Pages are view models composed from reusable row items
(`ToggleRowViewModel`, `ActionRowViewModel`, `ProcessRowViewModel`, …). A small set of implicit
`DataTemplate`s renders every row type, so a new tool page normally needs a view model and no
new XAML. The Power Plans switcher, dashboard, installed-app list and debloat selector have
dedicated views because their layouts are genuinely different.

Scrolling is handled by `SmoothScroll`: wheel events feed a target offset, and one
`CompositionTarget.Rendering` subscription eases every active `ScrollViewer` toward it. No timer
is allocated per wheel event, and the easing is frame-rate independent. Page navigation uses a
short fade/slide on the content host; theme changes use a brief opacity pulse while the new
dictionary settles. Search is debounced by 80 ms to avoid rebuilding the result list on every
keystroke.

The design system is three theme dictionaries plus one style dictionary and one template
dictionary. Every brush is a `DynamicResource`, so switching Dark/OLED/Light replaces one
dictionary and repaints instantly.
