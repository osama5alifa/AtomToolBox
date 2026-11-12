# AtomToolBox

[![Version](https://img.shields.io/badge/version-0.2.5-2f81f7?style=for-the-badge)](../../releases/latest)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![Tweaks](https://img.shields.io/badge/tweaks-234-22C55E?style=for-the-badge)](docs/FEATURE_MATRIX.md)
[![License](https://img.shields.io/badge/license-MIT-8B5CF6?style=for-the-badge)](LICENSE)

AtomToolBox is a Windows 10/11 utility toolbox. It brings system tweaks, maintenance tools,
shortcuts, startup control, context-menu management, power plans, app installation and backups
into one desktop app.

The current build is a native C#/.NET 8 WPF application. The old Electron version, 0.2.0, is
retired and kept only as the source for catalog regeneration.

## The project

AtomToolBox started as an Electron app with a large collection of batch and PowerShell tweaks.
The 0.2.5 rewrite keeps that catalog and the page structure, but replaces the runtime with a
single WPF process and a typed action engine.

The goal is not to bundle every Windows utility into one window. It is to make the common
changes easy to find, show their current state, apply them through native APIs where possible,
and keep a record that can be reversed.

The repository contains the C# implementation, the generated catalog, the 20 bundled power
plans, the shortcut list and the installer scripts. It does not contain the old Electron code.
Regenerating the catalog is optional; the generated files are committed.

## What is in the toolbox

### Tweaks

The catalog has 234 free entries across eight pages:

- **Windows** — 90 entries covering telemetry, services, Explorer behaviour, visual effects, taskbar and window settings, Edge and OneDrive removal, and the WinUtil-parity additions.
- **Gaming** — 15 entries for timer resolution, Game Mode, GPU scheduling, Game DVR, VBS and related latency settings.
- **GPU** — 28 entries for MPO, VSync, GPU priority, TDR, storage sense and related graphics settings.
- **Power** — 20 entries for sleep, hibernation, USB power saving, display timeouts and the Ultimate Performance plan.
- **Network** — 12 entries for IPv4/IPv6 preference, Teredo, Nagle, RSS, RSC, QoS, ECN and DNS.
- **Privacy** — 29 entries for app capabilities, activity history, location, Store suggestions, consumer features, Adobe URL blocking and Windows AI removal.
- **Personalize** — 21 entries for accent colours, taskbar behaviour, desktop icons and Explorer appearance.
- **Brave** — 19 entries for Brave Rewards, Wallet, VPN, Leo AI, telemetry, sync and background behaviour.

### System tools

Beyond the tweak pages, the app includes:

- **Dashboard** with CPU, memory, disk, GPU, uptime and system information.
- **Background Monitor** with process kill and image-file execution blocking.
- **Shortcuts** with 85 Windows tools and settings pages in 9 categories.
- **Startup Manager** using the native StartupApproved data.
- **Context Menu** management for shell verbs.
- **Windows Debloat** with a 37-entry AppX catalog and per-app removal.
- **Cleaner** with native cleanup categories and recycle-bin support.
- **Power Plans** with installed Windows schemes and all 20 legacy ready-made plans.
- **Backup & Restore** with restore points and registry exports.
- **App Installer** with 51 selected applications and download progress.
- **Installed Apps** with registry and AppX enumeration and uninstall.
- **History** with revert support and a registry audit export.
- **Settings** for Dark, OLED and Light themes, smooth scrolling and app data.

The ADVANCED entries are PRO-tagged pages. They open the Atom Optimizer prompt but no longer
contain a hidden catalog of PRO tweaks.

## Current release

**0.2.5** is the native rewrite release:

- All 234 tweak entries are free.
- The old 79-entry PRO catalog was removed.
- Twelve useful PRO entries were promoted into the free pages.
- Six WinUtil-parity tweaks were added: svchost splitting, BitLocker, Reserved Storage, device
  companion blocking, full Delivery Optimization disable and full MPO disable.
- The installer is self-contained and works offline.

The Windows page now has 90 entries, the Privacy page 29 and the GPU page 28. The PRO-tagged
pages remain as upgrade placeholders.

## Install

Download the current installer from
[GitHub Releases](https://github.com/osama5alifa/AtomToolBox/releases/latest). The file is about
51 MB and is not code-signed yet, so Windows SmartScreen may show a warning.

You can install over an existing 0.2.0 Electron installation. Setup removes the old
`C:\Program Files\AtomToolBox` directory and the old uninstall entry before it copies the new
files. Settings from the Electron app are not imported.

The installer writes to `%ProgramFiles%\AtomToolBox`, creates a public desktop shortcut and a
Start Menu shortcut, and adds an AtomToolBox entry to Apps & Features. User settings, history and
backups stay in `%LOCALAPPDATA%\AtomToolBox` when the app is uninstalled.

## Build

Requirements: Windows x64 and the .NET 8 SDK.

```powershell
./build.ps1 -Task Build        # compile
./build.ps1 -Task Test         # run the engine self-test
./build.ps1 -Task Publish      # framework-dependent portable build
./build.ps1 -Task Setup        # self-contained installer
```

`Setup` publishes a self-contained `win-x64` build and packages it with the IExpress tool that
ships with Windows. The output is `publish/AtomToolbox-Setup.exe`, with a versioned copy at
`publish/AtomToolbox-Setup-0.2.5.exe`. The .NET 8 `win-x64` runtime packs must be present in the
NuGet cache; after that, the build does not need a network connection.

Manual build:

```powershell
dotnet build src/AtomToolbox/AtomToolbox.csproj -c Release -p:UseSharedCompilation=false
```

## Project structure

```text
src/AtomToolbox/
  Atom.Toolbox.Domain/          Entities and value objects
  Atom.Toolbox.Application/     Use cases and interfaces
  Atom.Toolbox.Infrastructure/  Registry, services, powercfg, catalog, security
  Ui/                           WPF shell, views, view models, themes, animations
  Assets/                       Icon, logo and embedded helper scripts
  Program.cs                    Composition root, elevation and single instance
  SelfTest.cs                   Headless engine diagnostic

installer/                      Setup and uninstall scripts
tools/
  Atom.CatalogBuilder/          Builds the catalog from the old JSON and scripts
  Atom.LicenseIssuer/           Offline RSA licence tooling
  Atom.ResourceTransfer/        Adds the app icon and version to the installer
  catalog-report.txt            Catalog coverage report

docs/                           Architecture, feature matrix, security, QA, screenshots
```

## How it works

The tweak catalog is a JSON document embedded in the assembly. Each entry is a list of
operations: registry writes, service changes, `powercfg` calls, process launches, PowerShell or
batch fallbacks. The catalog is validated at startup and checked against a SHA-256 sidecar.

Registry changes are read back after they are written. Changes made through the app are recorded
in History, and the Backup page can create a restore point or export the registry before larger
changes.

The UI is WPF with a custom chrome, a fixed sidebar and a set of row templates. Pages are view
models built from the catalog; special pages such as Dashboard, Power Plans, Debloat and Installed
Apps have their own views. Scrolling uses a composition-driven animator, and navigation and theme
changes have short transitions.

## Catalog and the old build

The generated catalog, plans, shortcuts and debloat data are committed. To rebuild the base
catalog from the old Electron repository, clone
[the 0.2.0 repository](https://github.com/osama5alifa/AtomToolBox) and run:

```powershell
./tools/Generate-Catalog.ps1 -LegacyRoot C:\path\to\AtomToolBox
```

The builder preserves entries already present in `tweaks.json`, including the promoted and
WinUtil-parity additions, so regeneration does not drop them.

## QA and status

- Release build: 0 warnings, 0 errors.
- Engine self-test: 234 tweaks across 8 pages, 20 plans, 85 shortcuts, 4 installed schemes and
  18 context-menu entries.
- UI smoke test: all 21 pages open with no dispatcher exceptions.
- Theme switching, page transitions and the installer payload were tested on the reference VM.
- Warm start: around 760 to 780 ms, with a working set of about 126 MB for the
  framework-dependent build.

The installer is tested by extracting its payload, installing into a temporary directory and
running the uninstaller. Shortcut and HKLM writes need an interactive elevated session, so they
remain part of the release checklist.

Known gaps: the app is x64 only, the installer is unsigned, the four ADVANCED pages are
placeholders, and some removals use PowerShell because Windows exposes no practical managed API.
Windows App Debloat is destructive and cannot be undone from the app.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Feature matrix](docs/FEATURE_MATRIX.md)
- [Security model](docs/SECURITY.md)
- [QA notes](docs/QA.md)
- [Changelog](CHANGELOG.md)

## License

MIT. See [LICENSE](LICENSE).

## Safety

AtomToolBox changes Windows settings. Some tweaks affect services, security features and system
behaviour. Create a restore point before applying system-wide changes and read each tweak
description before turning it on.

BitLocker and Microsoft Edge removal are destructive. BitLocker decryption can take a long time
and cannot be re-enabled automatically from the app. Windows App Debloat removes selected
Store/MSIX apps for every user and removes provisioned copies; reinstall an app from the Microsoft
Store or its vendor if needed.
