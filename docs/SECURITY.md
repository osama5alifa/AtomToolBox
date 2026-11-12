# Security model

## Threat model

AtomToolBox runs elevated and edits security-relevant Windows settings. The realistic threats
are (a) a tampered binary or catalog executing unreviewed changes, (b) a forged PRO licence,
(c) casual reverse engineering of the paid boundary, and (d) a compromised dependency.
The app ships no third-party dependencies, which removes (d) almost entirely.

## Implemented layers

### Catalog integrity

- The generated `tweaks.json` is embedded as a managed resource.
- Its SHA-256 is generated at build time into `catalog.sha256` and verified on startup.
- The catalog is schema-validated before use: every action kind must be known, every page and
  tweak id unique. An invalid catalog aborts startup instead of executing unknown operations.

This detects accidental corruption and naive replacement of the catalog. A determined attacker
can patch the verification code too; the hash is a tripwire, not a cryptographic guarantee.

### Signed offline licensing

- PRO licences are `base64url(payload).base64url(signature)` tokens.
- The payload is signed with RSA-4096 / SHA-256 / PKCS#1 v1.5 by the vendor.
- Only the public key is embedded (`Catalog/license-public.pem`); the private key lives in
  `tools/keys/` and is git-ignored.
- The token is parsed only after signature validation, and expiry/tier are enforced.
- The community UI and the page factory both refuse PRO operations when `IsPro` is false, so a
  single bypassed UI check does not unlock a service.

Issue tokens with:

```powershell
dotnet run --project tools/Atom.LicenseIssuer -- issue `
  --private tools/keys/atom-license-private.pem `
  --licensee "Customer" --tier pro --expires 2027-12-31
```

### Runtime hardening

- Production manifest requests `requireAdministrator`; a runtime guard also relaunches elevated
  when the manifest was bypassed.
- Single-instance mutex prevents duplicate elevated processes.
- Anti-debug check (`Debugger.IsAttached` / `kernel32!IsDebuggerPresent`) is part of integrity
  reporting. It is a deterrence, not a sandbox.
- All external input is validated: GUIDs for power plans, service names, registry hives/paths,
  package-name patterns for AppX, file names for restore/import, and download URLs restricted
  to HTTP(S). No UI string is interpolated into a PowerShell command except catalog data that
  was validated at build time.
- Unhandled exceptions are logged to `%LOCALAPPDATA%\AtomToolBox\logs\app.log`; the process does
  not continue after a fatal startup error.

### Least surprise / reversibility

- Tweaks use the same mechanisms as Windows Settings/Task Manager (StartupApproved, service
  APIs, powercfg, registry), not file deletion of system binaries.
- Startup disabling never deletes Run values.
- IFEO blocking stores and restores the previous `Debugger` value.
- The undo history records every change and can revert entries.
- Power-plan import/duplicate/delete are verified against `powercfg /list`.

## Release hardening checklist

1. **Strong-name / Authenticode sign** the executable with the Project Atom certificate. Verify
   with `signtool verify /pa /all`.
2. **Obfuscate** the release assembly (e.g., commercial .NET obfuscator) after enabling
   deterministic builds. Keep the domain model names if support diagnostics matter; obfuscate
   the licensing and engine internals.
3. **Enable ReadyToRun** (`-p:PublishReadyToRun=true`) and ship a self-contained build if the
   target machines do not have the .NET 8 Desktop Runtime.
4. **Protect the private licence key** in an HSM or CI secret; never commit `tools/keys/`.
5. **Server-side entitlement** for high-value commerce: the offline signature proves the token
   was issued by the vendor, but revocation requires an online check. Add a periodic,
   privacy-respecting validation endpoint for enterprise licences.
6. **Reproducible build**: `Atom.CatalogBuilder` writes a timestamp into `tweaks.json`; pin or
   remove it if bit-for-bit reproducibility is required.

## Installer

`AtomToolbox-Setup.exe` is a self-contained IExpress package: it carries the .NET 8 runtime,
installs to `%ProgramFiles%\AtomToolBox`, creates shortcuts and registers an uninstall entry.
It does not download anything. The Setup executable and the application are **not Authenticode
signed in this repository**; sign both with the Project Atom certificate before public release.
The setup/uninstall scripts run with administrator rights and only touch the install directory,
the two shortcuts and the uninstall/App Paths registry keys.

## Honest limits

Any client-side gate can be bypassed by an attacker with full control of the machine. The
design goal is to make casual patching expensive and to keep the premium logic behind a
signature check that cannot be forged without the private key. It is not DRM for a hostile
administrator, and it is not a substitute for code signing.
