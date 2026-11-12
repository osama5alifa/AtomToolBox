# Changelog

## 0.2.5 — native rewrite

- Complete C#/.NET 8 WPF rewrite of the Electron 0.2.x application.
- Single native assembly; no Electron, Node.js, WebView or third-party NuGet packages.
- Typed tweak catalog:
  - 234 tweaks, all free.
  - 251 registry writes, 27 service operations, 45 powercfg operations.
  - The 79-entry PRO catalog was removed; the PRO-tagged pages remain as placeholders.
  - 12 useful PRO entries were promoted into the free pages.
  - Six WinUtil-parity entries added: svchost splitting, BitLocker, Reserved Storage, device
    companion blocking, full Delivery Optimization disable and full MPO disable.
  - 11 PowerShell and 1 batch action remain for AppX removal, OneDrive, Edge, BitLocker, AI,
    svchost, DNS and legacy helper cases.
- Native implementations for registry, service control, power plans, timer resolution, startup
  approval, context-menu verbs, accent colours, cleanup, restore points and hardware probes.
- Native read-back verification for registry tweaks; 16-way parallel state scans.
- Power Plans: focused switcher with installed schemes, active badge, one-click activation and
  the full set of 20 legacy ready-made plans. The settings editor, timer and plan CRUD were
  removed by request.
- Dashboard, Background Monitor, Startup Manager, Debloat, Cleaner, App Installer, Installed
  Apps, Backup & Restore, History, Settings, Shortcuts and all eight free tweak pages.
- UI rebuilt to the legacy dark design with 1280×800 shell, grouped sidebar, status strip,
  global search, toast notifications and Dark/OLED/Light themes.
- Premium boundary: PRO pages still show the upgrade state, but no PRO tweak definitions ship
  in the catalog. The RSA licence code remains for future use.
- Hardening: catalog SHA-256 integrity check, anti-debug detection, elevation guard,
  single-instance activation, validated inputs, structured logging.
- Fixes over the legacy build:
  - Power-plan APPLY no longer duplicates the plan on every click.
  - Accent/startup/context-menu/cleaner no longer spawn PowerShell.
  - Three inert context-menu entries removed.
  - Page scroll resets on navigation; keyboard-accessible toggles.
  - Power-plan GUID parsing and selection repaired.
  - `Warning` resource collision removed.
- Smooth UI: composition-driven smooth scrolling, page fade/slide transitions, theme pulse and
  debounced search. No per-wheel timers.
- Offline installer: self-contained .NET 8 `win-x64` payload packaged into a single branded
  `AtomToolbox-Setup-0.2.5.exe` with desktop/Start Menu shortcuts and an uninstall entry.
- Removed the in-repository legacy Electron tree; catalog regeneration points at the GitHub
  repository instead.
- QA: headless `--self-test`, 21-page UI Automation smoke test, theme switch verification and a
  full installer/uninstaller cycle into a temporary directory.

## Legacy 0.2.0

See the original [AtomToolBox repository](https://github.com/osama5alifa/AtomToolBox).
