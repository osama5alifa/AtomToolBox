#Requires -Version 5.1
<#
    AtomToolBox 0.2.5 installer (runs elevated; invoked by the SFX package).
    Installs the self-contained payload, creates shortcuts and registers an uninstaller.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = '',
    [switch]$NoElevate,
    [switch]$SkipShellIntegration,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = if ($env:ATOM_INSTALL_DIR) { $env:ATOM_INSTALL_DIR } else { Join-Path $env:ProgramFiles 'AtomToolBox' }
}
if ($env:ATOM_SKIP_SHELL -eq '1') { $SkipShellIntegration = $true }
if ($env:ATOM_NO_ELEVATE -eq '1') { $NoElevate = $true }
if ($env:ATOM_QUIET -eq '1') { $Quiet = $true }
$logPath = Join-Path $env:TEMP 'AtomToolbox-Setup.log'

function Write-Step([string]$message) {
    $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $message
    Write-Host $line
    try { Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8 } catch { }
}

function Test-Admin {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch {
        return $false
    }
}

function Show-Message([string]$text, [string]$title, [string]$icon = 'Information') {
    if ($Quiet) { return }
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        $kind = if ($icon -eq 'Error') { [System.Windows.Forms.MessageBoxIcon]::Error } else { [System.Windows.Forms.MessageBoxIcon]::Information }
        [System.Windows.Forms.MessageBox]::Show($text, $title, [System.Windows.Forms.MessageBoxButtons]::OK, $kind) | Out-Null
    } catch { }
}

function New-AppShortcut([string]$shortcutPath, [string]$targetPath, [string]$workingDirectory) {
    $parent = Split-Path -Parent $shortcutPath
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $targetPath
        $shortcut.WorkingDirectory = $workingDirectory
        $shortcut.IconLocation = "$targetPath,0"
        $shortcut.Description = 'AtomToolBox 0.2.5'
        $shortcut.Save()
    } finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

try {
    Write-Step "AtomToolBox 0.2.5 setup starting."
    Write-Step "Install directory: $InstallDir"

    if (-not $NoElevate -and -not (Test-Admin)) {
        Write-Step 'Requesting administrator rights...'
        $arguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', ('"{0}"' -f $PSCommandPath),
            '-InstallDir', ('"{0}"' -f $InstallDir)
        )
        if ($SkipShellIntegration) { $arguments += '-SkipShellIntegration' }
        if ($Quiet) { $arguments += '-Quiet' }

        $elevated = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        exit $elevated.ExitCode
    }

    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'AtomToolBox requires 64-bit Windows 10 or Windows 11.'
    }
    if ([Environment]::OSVersion.Version.Build -lt 17763) {
        throw 'AtomToolBox requires Windows 10 version 1809 (build 17763) or newer.'
    }

    $source = Join-Path $PSScriptRoot 'payload'
    $flatLayout = $false
    if (-not (Test-Path -LiteralPath (Join-Path $source 'AtomToolbox.exe'))) {
        # IExpress packages are flat; the self-extracting package puts every file in one folder.
        $source = $PSScriptRoot
        $flatLayout = $true
    }

    $sourceExe = Join-Path $source 'AtomToolbox.exe'
    if (-not (Test-Path -LiteralPath $sourceExe)) {
        throw "The installer payload is missing AtomToolbox.exe ($sourceExe)."
    }

    Write-Step 'Stopping any running AtomToolBox instance...'
    Get-Process -Name AtomToolbox -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    # Remove the legacy Electron uninstall registration if this is an in-place upgrade.
    foreach ($legacyKey in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AtomToolBox 0.2.0',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\AtomToolBox 0.2.0')) {
        if (Test-Path -LiteralPath $legacyKey) {
            Remove-Item -LiteralPath $legacyKey -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Step 'Preparing installation directory...'
    $backupDir = $null
    if (Test-Path -LiteralPath $InstallDir) {
        $removed = $false
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            try {
                Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction Stop
                $removed = $true
                break
            } catch {
                if ($attempt -eq 4) {
                    $backupDir = "$InstallDir.old-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
                    try {
                        Move-Item -LiteralPath $InstallDir -Destination $backupDir -ErrorAction Stop
                        Write-Step "Old installation moved to $backupDir"
                    } catch {
                        throw "Could not replace $InstallDir. Close AtomToolBox (including the old 0.2.0 app), then retry. $($_.Exception.Message)"
                    }
                } else {
                    Start-Sleep -Milliseconds 400
                }
            }
        }
        if (-not $removed -and -not $backupDir) {
            throw "Could not replace $InstallDir."
        }
    }
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

    Write-Step 'Copying application files...'
    if ($flatLayout) {
        Get-ChildItem -LiteralPath $source -File | Where-Object {
            $_.Name -notin @('setup.cmd', 'setup.ps1', 'uninstall.ps1', 'install.sed')
        } | Copy-Item -Destination $InstallDir -Force
    } else {
        Copy-Item -Path (Join-Path $source '*') -Destination $InstallDir -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $InstallDir 'uninstall.ps1') -Force

    $targetExe = Join-Path $InstallDir 'AtomToolbox.exe'
    if (-not (Test-Path -LiteralPath $targetExe)) {
        throw 'The application files could not be installed.'
    }

    if (-not $SkipShellIntegration) {
        Write-Step 'Creating desktop and Start Menu shortcuts...'
        $desktop = [Environment]::GetFolderPath('CommonDesktopDirectory')
        $programs = [Environment]::GetFolderPath('CommonPrograms')
        if ([string]::IsNullOrWhiteSpace($desktop)) { $desktop = [Environment]::GetFolderPath('DesktopDirectory') }
        if ([string]::IsNullOrWhiteSpace($programs)) { $programs = [Environment]::GetFolderPath('Programs') }

        New-AppShortcut (Join-Path $desktop 'AtomToolBox.lnk') $targetExe $InstallDir
        New-AppShortcut (Join-Path (Join-Path $programs 'AtomToolBox') 'AtomToolBox.lnk') $targetExe $InstallDir

        Write-Step 'Registering uninstall information...'
        $uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AtomToolBox'
        if (Test-Path -LiteralPath $uninstallKey) {
            Remove-Item -LiteralPath $uninstallKey -Recurse -Force
        }
        New-Item -Path $uninstallKey -Force | Out-Null

        $estimatedKb = 0
        try {
            $bytes = (Get-ChildItem -LiteralPath $InstallDir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
            if ($bytes) { $estimatedKb = [int]($bytes / 1KB) }
        } catch { }

        $uninstallCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f (Join-Path $InstallDir 'uninstall.ps1')
        $properties = @{
            DisplayName = 'AtomToolBox'
            DisplayVersion = '0.2.5'
            Publisher = 'Project Atom'
            InstallLocation = $InstallDir
            DisplayIcon = "$targetExe,0"
            UninstallString = $uninstallCommand
            QuietUninstallString = $uninstallCommand + ' -Quiet'
            InstallDate = (Get-Date).ToString('yyyyMMdd')
            EstimatedSize = $estimatedKb
            NoModify = 1
            NoRepair = 1
        }
        foreach ($name in $properties.Keys) {
            $value = $properties[$name]
            if ($value -is [int]) {
                New-ItemProperty -Path $uninstallKey -Name $name -Value $value -PropertyType DWord -Force | Out-Null
            } else {
                New-ItemProperty -Path $uninstallKey -Name $name -Value ([string]$value) -PropertyType String -Force | Out-Null
            }
        }

        $appPathKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\AtomToolbox.exe'
        if (-not (Test-Path -LiteralPath $appPathKey)) {
            New-Item -Path $appPathKey -Force | Out-Null
        }
        Set-ItemProperty -Path $appPathKey -Name '(default)' -Value $targetExe
        Set-ItemProperty -Path $appPathKey -Name 'Path' -Value $InstallDir
    }

    if ($backupDir) {
        Write-Step "Previous installation kept at $backupDir"
    }
    Write-Step 'Installation complete.'
    Show-Message ("AtomToolBox 0.2.5 is installed.{0}{0}Desktop and Start Menu shortcuts were created." -f [Environment]::NewLine) 'AtomToolBox Setup'
    exit 0
} catch {
    Write-Step ('ERROR: ' + $_.Exception.Message)
    Show-Message ('AtomToolBox could not be installed.' + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message) 'AtomToolBox Setup' 'Error'
    exit 1
}
