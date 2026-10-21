# AtomToolBox
[![Version](https://img.shields.io/badge/version-0.2.0-blue.svg)](https://github.com/osama5alifa/AtomToolBox/releases)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows)](https://github.com/osama5alifa/AtomToolBox)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)


## Overview
AtomToolBox brings common Windows tweaks, maintenance tools and system controls into one desktop app. It is designed for both everyday users and power users, with plain-language descriptions, live state checks and reversible changes.

The four entries in the **ADVANCED** group are intentionally locked in this build. They are part of Atom Optimizer and open a PRO prompt instead of a tool page.

## Features
- Windows, Gaming, GPU, Power, Network, Privacy and Personalize tweaks
- Background Monitor with process kill and application blocking
- Startup Manager using native StartupApproved behaviour
- Context Menu add/remove verbs
- Windows App Debloat with per-app selection, all-user removal and provisioned-package cleanup
- Brave Debloater and system Cleaner
- Power Plans: create, import, export, edit everyday settings and timer resolution
- Backup & Restore: restore points and registry backups
- App Installer and Installed Apps
- Global search, live CHECK STATE and registry audit export
- Dark, OLED and Light themes

## Requirements
- Windows 10 or Windows 11, x64
- Node.js 18 or newer for development
- Administrator rights; AtomToolBox always runs elevated and asks for UAC approval at launch

## Releases
Download the current installer from [GitHub Releases](https://github.com/ProjectAtomOS/Atom-Tool-Box/releases).
## QA and screenshots

## Documentation
- [Audit report](docs/AUDIT_REPORT.md)
- [Documentation index](docs/README.md)
- [Changelog](CHANGELOG.md)
- [Release notes for 0.2.0](docs/RELEASE_NOTES_v0.2.0.md)
- [Windows App Debloat guide](docs/DEBLOAT.md)
- [Security policy](SECURITY.md)

## License
MIT — see [LICENSE](LICENSE).

## Safety
AtomToolBox changes Windows settings. Some tweaks affect services, security features and system behaviour. Create a restore point before applying system-wide changes, and review each tweak description before turning it on.

Windows App Debloat is intentionally destructive: selected Store/MSIX apps are removed for every user, and their provisioned copies are removed so Windows does not reinstall them for new accounts. Removal is not reversible from AtomToolBox. Reinstall an app from the Microsoft Store or its vendor if needed. Read [docs/DEBLOAT.md](docs/DEBLOAT.md) before using it.
