#Requires -Version 5.1
<#
    AtomToolBox 0.2.5 uninstaller.
    Removes the application, shortcuts and registration. Per-user data in %APPDATA% is kept.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'AtomToolBox'),
    [switch]$Quiet,
    [switch]$NoElevate
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $env:TEMP 'AtomToolbox-Uninstall.log'

function Write-Step([string]$message) {
    try { Add-Content -LiteralPath $logPath -Value ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $message) -Encoding UTF8 } catch { }
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

try {
    # Never delete the directory while this script is executing from inside it.
    if ($PSCommandPath.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase)) {
        $tempScript = Join-Path $env:TEMP ('AtomToolbox-uninstall-{0}.ps1' -f [Guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath $PSCommandPath -Destination $tempScript -Force
        $arguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', ('"{0}"' -f $tempScript),
            '-InstallDir', ('"{0}"' -f $InstallDir)
        )
        if ($Quiet) { $arguments += '-Quiet' }
        if (Test-Admin) {
            $arguments += '-NoElevate'
            Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Wait | Out-Null
        } else {
            Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait | Out-Null
        }
        Remove-Item -LiteralPath $tempScript -Force -ErrorAction SilentlyContinue
        exit 0
    }

    if (-not $NoElevate -and -not (Test-Admin)) {
        $arguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', ('"{0}"' -f $PSCommandPath),
            '-InstallDir', ('"{0}"' -f $InstallDir)
        )
        if ($Quiet) { $arguments += '-Quiet' }
        $elevated = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        exit $elevated.ExitCode
    }

    if (-not $Quiet) {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "Remove AtomToolBox 0.2.5 from this PC?`n`nYour settings and history in %APPDATA% will be kept.",
            'AtomToolBox Uninstall',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question)
        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) { exit 0 }
    }

    Write-Step 'Stopping AtomToolBox...'
    Get-Process -Name AtomToolbox -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    Write-Step 'Removing shortcuts...'
    $desktop = [Environment]::GetFolderPath('CommonDesktopDirectory')
    $programs = [Environment]::GetFolderPath('CommonPrograms')
    if ([string]::IsNullOrWhiteSpace($desktop)) { $desktop = [Environment]::GetFolderPath('DesktopDirectory') }
    if ([string]::IsNullOrWhiteSpace($programs)) { $programs = [Environment]::GetFolderPath('Programs') }
    Remove-Item -LiteralPath (Join-Path $desktop 'AtomToolBox.lnk') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path (Join-Path $programs 'AtomToolBox') 'AtomToolBox.lnk') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $programs 'AtomToolBox') -Force -ErrorAction SilentlyContinue

    Write-Step 'Removing registration...'
    Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AtomToolBox' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\AtomToolbox.exe' -Recurse -Force -ErrorAction SilentlyContinue

    Write-Step 'Removing application files...'
    if (Test-Path -LiteralPath $InstallDir) {
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            try {
                Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction Stop
                break
            } catch {
                if ($attempt -eq 4) { throw }
                Start-Sleep -Milliseconds 400
            }
        }
    }

    Write-Step 'Uninstall complete.'
    Show-Message 'AtomToolBox 0.2.5 has been removed.' 'AtomToolBox Uninstall'
    exit 0
} catch {
    Write-Step ('ERROR: ' + $_.Exception.Message)
    Show-Message ('AtomToolBox could not be completely removed.' + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message) 'AtomToolBox Uninstall' 'Error'
    exit 1
}
