# AtomToolBox

[![Version](https://img.shields.io/badge/version-0.2.5-2f81f7?style=for-the-badge)](../../releases/latest)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![Tweaks](https://img.shields.io/badge/tweaks-295-22C55E?style=for-the-badge)](docs/FEATURE_MATRIX.md)
[![License](https://img.shields.io/badge/license-MIT-8B5CF6?style=for-the-badge)](LICENSE)

A Windows 10/11 x64 tweak and system utility toolbox. This repository is the C#/.NET 8 rewrite of the Electron-based 0.2.0 app.

**Version:** 0.2.5

## Overview

AtomToolBox puts the tweaks, maintenance tools and system controls from 0.2.0 in one desktop app. The rewrite uses WPF and Win32 APIs instead of Electron, Node and a web view. Tweaks are stored in a typed catalog, registry changes are read back after they are written, and changes made through the app are recorded in History.

The four entries in the ADVANCED group are still locked. They are part of Atom Optimizer and open a PRO prompt instead of a tool page.

## What changed in 0.2.5

The main change is the rewrite itself. The app is now one native process, opens faster than the Electron build and does not ship a browser runtime.

Power Plans was cut back to plan switching. The settings editor, timer controls and create/import/export/delete actions are gone. The page lists the installed Windows schemes, shows the active one and lets you switch with one click. All 20 ready-made plans from 0.2.0 are still included. Applying one imports it once, caches the GUID and activates it.

Scrolling uses a single composition-driven animator instead of a timer per wheel event. Page changes fade and slide in, and theme changes get a short fade while the new dictionary is applied.

The installer is self-contained. It carries the .NET 8 runtime, so the target PC does not need .NET installed. It installs to Program Files, creates desktop and Start Menu shortcuts, and registers an uninstaller.

## Features

- Windows, Gaming, GPU, Power, Network, Privacy and Personalize tweaks
- Background Monitor with process kill and application blocking
- Shortcuts page with 85 Windows tools in 9 categories
- Startup Manager using native StartupApproved behaviour
- Context Menu add/remove verbs
- Windows App Debloat with per-app selection and all-user removal
- Brave Debloater and system Cleaner
- Power Plans with the installed schemes and all 20 ready-made plans
- Backup & Restore with restore points and registry backups
- App Installer, Installed Apps, History and registry audit export
- Global search, live CHECK STATE and Dark, OLED and Light themes

## Requirements

- Windows 10 (1809 or newer) or Windows 11, x64
- Administrator rights. The app always runs elevated and asks for UAC approval at launch.
- The installer needs no .NET runtime. The portable framework-dependent build needs the .NET 8 Desktop Runtime.
- Building from source needs the .NET 8 SDK.

## Install

Download the current installer from [GitHub Releases](https://github.com/osama5alifa/AtomToolBox/releases/latest). The file is about 51 MB and is not code-signed yet, so Windows SmartScreen may show a warning.

You can install over an existing 0.2.0 Electron installation. Setup removes the old `C:\Program Files\AtomToolBox` directory and the old uninstall entry before it copies the new files. Settings from the Electron app are not imported.

The installer writes to `%ProgramFiles%\AtomToolBox`, creates a public desktop shortcut and a Start Menu shortcut, and adds an AtomToolBox entry to Apps & Features. User settings, history and backups stay in `%LOCALAPPDATA%\AtomToolBox` when the app is uninstalled.

## Build

```powershell
./build.ps1 -Task Build        # compile
./build.ps1 -Task Test         # run the engine self-test
./build.ps1 -Task Publish      # framework-dependent portable build
./build.ps1 -Task Setup        # self-contained installer
```

`Setup` publishes a self-contained `win-x64` build and packages it with the IExpress tool that ships with Windows. The output is `publish/AtomToolbox-Setup.exe`, with a versioned copy at `publish/AtomToolbox-Setup-0.2.5.exe`. The .NET 8 `win-x64` runtime packs must be present in the NuGet cache; after that, the build does not need a network connection.

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
  Atom.CatalogBuilder/          Builds tweaks.json from the old JSON and batch scripts
  Atom.LicenseIssuer/           Offline RSA licence tooling
  Atom.ResourceTransfer/        Adds the app icon and version to the installer
  catalog-report.txt            Catalog coverage report

docs/                           Architecture, feature matrix, security, QA, screenshots
```

The generated catalog, plans, shortcuts and debloat data are committed. To rebuild them, clone the [0.2.0 Electron repository](https://github.com/osama5alifa/AtomToolBox) and run:

```powershell
./tools/Generate-Catalog.ps1 -LegacyRoot C:\path\to\AtomToolBox
```

## QA

The release build passes the engine self-test: 295 tweaks across 10 pages, 216 free tweaks, 20 ready-made plans, 85 shortcuts in 9 categories, 4 installed power schemes and 18 context-menu entries. The registry write check is skipped in sandboxes that block HKCU writes.

The UI smoke test opens all 21 pages through UI Automation and checks for dispatcher exceptions. Theme switching and page transitions were tested on the reference VM. The installer was tested by extracting its payload, installing to a temporary directory and running the uninstaller. Shortcut and HKLM writes need an interactive elevated session, so they remain part of the release checklist.

Warm start on the reference VM was around 760 to 780 ms, with a working set of about 126 MB for the framework-dependent build. Most of that time is WPF and CLR startup.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Feature matrix](docs/FEATURE_MATRIX.md)
- [Security model](docs/SECURITY.md)
- [QA notes](docs/QA.md)
- [Changelog](CHANGELOG.md)

## License

MIT. See [LICENSE](LICENSE).

## Safety

AtomToolBox changes Windows settings. Some tweaks affect services, security features and system behaviour. Create a restore point before applying system-wide changes and read each tweak description before turning it on.

Windows App Debloat is destructive. Selected Store/MSIX apps are removed for every user, and provisioned copies are removed so Windows does not reinstall them for new accounts. Removal cannot be undone from AtomToolBox. Reinstall an app from the Microsoft Store or its vendor if needed.
