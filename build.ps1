#Requires -Version 5.1
<#
.SYNOPSIS
    Build, test, publish and package AtomToolBox 0.2.5.
.DESCRIPTION
    Setup creates an offline, single-file installer (AtomToolbox-Setup-0.2.5.exe) from a
    self-contained win-x64 publish. No .NET runtime is required on the target PC.
#>
[CmdletBinding()]
param(
    [ValidateSet('Build', 'Test', 'Publish', 'Setup', 'PublishSingleFile', 'PublishSelfContained', 'Clean')]
    [string]$Task = 'Build',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\AtomToolbox\AtomToolbox.csproj'
$dotnet = 'dotnet'
$common = @('-p:UseSharedCompilation=false', '-p:RestoreIgnoreFailedSources=true')
$version = '0.2.5'

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Write-Host "> dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE"
    }
}

switch ($Task) {
    'Clean' {
        Invoke-DotNet -Arguments @('clean', $project, '-c', $Configuration)
        foreach ($path in @('publish', 'artifacts', 'installer\stage')) {
            $full = Join-Path $root $path
            if (Test-Path -LiteralPath $full) {
                Remove-Item -LiteralPath $full -Recurse -Force
            }
        }
        Write-Host 'Clean complete.' -ForegroundColor Green
        break
    }

    'Build' {
        Invoke-DotNet (@('restore', $project, '-p:RestoreUseStaticGraphEvaluation=true') + $common)
        Invoke-DotNet (@('build', $project, '-c', $Configuration, '--no-restore') + $common)
        Write-Host "Build complete: $root\src\AtomToolbox\bin\$Configuration\net8.0-windows\AtomToolbox.exe" -ForegroundColor Green
        break
    }

    'Test' {
        Invoke-DotNet (@('build', $project, '-c', 'Debug', '-p:AtomNoElevation=true') + $common)
        $dll = Join-Path $root 'src\AtomToolbox\bin\Debug\net8.0-windows\AtomToolbox.dll'
        & $dotnet $dll --self-test
        break
    }

    'Publish' {
        $out = Join-Path $root "publish\AtomToolbox-$version"
        Invoke-DotNet (@('publish', $project, '-c', 'Release', '--no-restore', '-o', $out) + $common)
        Write-Host "Published folder build: $out" -ForegroundColor Green
        break
    }

    'Setup' {
        $selfContained = Join-Path $root 'publish\self-contained'
        $stage = Join-Path $env:TEMP ('AtomToolbox-install-' + [Guid]::NewGuid().ToString('N'))
        $setupExe = Join-Path $root "publish\AtomToolbox-Setup-$version.exe"

        if ($stage -match ' ') {
            $stage = Join-Path $env:PUBLIC ('AtomToolbox-install-' + [Guid]::NewGuid().ToString('N'))
        }

        Write-Host 'Restoring the self-contained win-x64 runtime...' -ForegroundColor Cyan
        Invoke-DotNet (@('restore', $project, '-r', 'win-x64', '-p:RestoreIgnoreFailedSources=true') + $common)

        Write-Host 'Publishing the self-contained application...' -ForegroundColor Cyan
        if (Test-Path -LiteralPath $selfContained) { Remove-Item -LiteralPath $selfContained -Recurse -Force }
        Invoke-DotNet (@('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false',
            '-p:DebugType=None', '-p:DebugSymbols=false', '--no-restore', '-o', $selfContained) + $common)

        $appExe = Join-Path $selfContained 'AtomToolbox.exe'
        if (-not (Test-Path -LiteralPath $appExe)) {
            throw "Self-contained publish did not produce AtomToolbox.exe ($appExe)."
        }

        # Never ship a debug/no-elevation manifest by accident.
        $bytes = [IO.File]::ReadAllBytes($appExe)
        $latin = [Text.Encoding]::GetEncoding(1252).GetString($bytes)
        if ($latin.IndexOf('requireAdministrator') -lt 0) {
            throw 'The published application does not request administrator rights (wrong manifest).'
        }

        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        New-Item -ItemType Directory -Path $stage -Force | Out-Null

        Write-Host 'Staging a flat IExpress payload...' -ForegroundColor Cyan
        Copy-Item -Path (Join-Path $selfContained '*') -Destination $stage -Force
        foreach ($file in @('setup.cmd', 'setup.ps1', 'uninstall.ps1')) {
            Copy-Item -LiteralPath (Join-Path $root "installer\$file") -Destination (Join-Path $stage $file) -Force
        }

        $files = Get-ChildItem -LiteralPath $stage -File |
            Where-Object { $_.Name -ne 'install.sed' } |
            Sort-Object Name
        $strings = New-Object System.Collections.Generic.List[string]
        $sources = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt $files.Count; $i++) {
            $strings.Add(('FILE{0}="{1}"' -f $i, $files[$i].Name))
            $sources.Add(('%FILE{0}%=' -f $i))
        }

        $sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$stage\AtomToolbox-Setup.exe
FriendlyName=AtomToolBox $version
AppLaunched=setup.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[Strings]
$($strings -join "`r`n")
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
$($sources -join "`r`n")
"@
        $sedPath = Join-Path $stage 'install.sed'
        [IO.File]::WriteAllText($sedPath, $sed, [Text.ASCIIEncoding]::new())

        Write-Host 'Building the single-file installer with IExpress...' -ForegroundColor Cyan
        $iexpress = Join-Path $env:WINDIR 'System32\iexpress.exe'
        if (-not (Test-Path -LiteralPath $iexpress)) {
            throw 'IExpress is not available on this Windows installation.'
        }
        $process = Start-Process -FilePath $iexpress -ArgumentList @('/N', '/Q', $sedPath) -Wait -PassThru
        if ($process.ExitCode -ne 0) {
            throw "IExpress exited with code $($process.ExitCode)."
        }

        $built = Join-Path $stage 'AtomToolbox-Setup.exe'
        if (-not (Test-Path -LiteralPath $built)) {
            throw 'IExpress did not produce AtomToolbox-Setup.exe.'
        }

        if (Test-Path -LiteralPath $setupExe) { Remove-Item -LiteralPath $setupExe -Force }
        Copy-Item -LiteralPath $built -Destination $setupExe -Force

        try {
            $brander = Join-Path $root 'tools\Atom.ResourceTransfer\Atom.ResourceTransfer.csproj'
            & $dotnet run --project $brander -c Release -- --source $appExe --target $setupExe
            if ($LASTEXITCODE -eq 0) {
                Write-Host 'Installer branded with the AtomToolBox icon and version.' -ForegroundColor DarkGray
            }
        } catch {
            Write-Warning "Installer branding skipped: $($_.Exception.Message)"
        }

        $latestSetup = Join-Path $root 'publish\AtomToolbox-Setup.exe'
        if (Test-Path -LiteralPath $latestSetup) { Remove-Item -LiteralPath $latestSetup -Force }
        try {
            New-Item -ItemType HardLink -Path $latestSetup -Target $setupExe -ErrorAction Stop | Out-Null
        } catch {
            Copy-Item -LiteralPath $setupExe -Destination $latestSetup -Force
        }

        $sizeMb = [math]::Round((Get-Item -LiteralPath $setupExe).Length / 1MB, 1)
        Remove-Item -LiteralPath $stage -Recurse -Force
        Write-Host "Installer ready: $setupExe ($sizeMb MB)" -ForegroundColor Green
        Write-Host 'It bundles the .NET 8 runtime; no download or prerequisite is required.' -ForegroundColor Green
        break
    }
    'PublishSingleFile' {
        # Requires the ILLink/bundler NuGet task, which is not present in an offline SDK cache.
        $out = Join-Path $root 'publish\framework-dependent-single-file'
        Invoke-DotNet (@('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $out) + $common)
        Write-Host "Published single-file build: $out" -ForegroundColor Green
        break
    }

    'PublishSelfContained' {
        $out = Join-Path $root 'publish\self-contained'
        Invoke-DotNet (@('restore', $project, '-r', 'win-x64', '-p:RestoreIgnoreFailedSources=true') + $common)
        Invoke-DotNet (@('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false',
            '-p:DebugType=None', '-p:DebugSymbols=false', '--no-restore', '-o', $out) + $common)
        Write-Host "Published self-contained build: $out" -ForegroundColor Green
        break
    }
}
