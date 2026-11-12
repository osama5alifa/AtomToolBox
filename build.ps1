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

        Write-Host 'Staging 7-Zip SFX payload...' -ForegroundColor Cyan
        $payload = Join-Path $stage 'payload'
        New-Item -ItemType Directory -Path $payload -Force | Out-Null
        Copy-Item -Path (Join-Path $selfContained '*') -Destination $payload -Recurse -Force
        foreach ($file in @('setup.cmd', 'setup.ps1', 'uninstall.ps1', 'config.txt')) {
            Copy-Item -LiteralPath (Join-Path $root "installer\$file") -Destination (Join-Path $stage $file) -Force
        }

        $module = Join-Path $root 'installer\7zSD.sfx'
        if (-not (Test-Path -LiteralPath $module)) { throw 'installer\7zSD.sfx is missing.' }
        $branded = Join-Path $stage '7zSD-branded.sfx'
        Copy-Item -LiteralPath $module -Destination $branded -Force
        $brander = Join-Path $root 'tools\Atom.ResourceTransfer\Atom.ResourceTransfer.csproj'
        & $dotnet run --project $brander -c Release -- --source $appExe --target $branded
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $branded)) {
            throw 'Could not brand the 7-Zip installer module.'
        }

        $sevenZip = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
        if (-not (Test-Path -LiteralPath $sevenZip)) {
            $sevenZip = Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe'
        }
        if (-not (Test-Path -LiteralPath $sevenZip)) { throw '7-Zip was not found.' }

        Write-Host 'Building the 7-Zip SFX installer...' -ForegroundColor Cyan
        if (Test-Path -LiteralPath $setupExe) { Remove-Item -LiteralPath $setupExe -Force }
        $archive = Join-Path $stage 'payload.7z'
        Push-Location $stage
        try {
            & $sevenZip a -t7z -mx=9 -mmt=on $archive 'setup.cmd' 'setup.ps1' 'uninstall.ps1' 'payload'
            if ($LASTEXITCODE -ne 0) { throw "7-Zip archive exited with code $LASTEXITCODE" }
        } finally {
            Pop-Location
        }
        if (-not (Test-Path -LiteralPath $archive)) { throw 'The payload archive was not created.' }

        # 7zSD expects: SFX stub + config.txt + archive.7z, concatenated.
        $output = [IO.File]::Create($setupExe)
        try {
            foreach ($part in @($branded, (Join-Path $root 'installer\config.txt'), $archive)) {
                $input = [IO.File]::OpenRead($part)
                try { $input.CopyTo($output) } finally { $input.Dispose() }
            }
        } finally {
            $output.Dispose()
        }
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }

        if (-not (Test-Path -LiteralPath $setupExe)) { throw 'The installer executable was not created.' }
        $latestSetup = Join-Path $root 'publish\AtomToolbox-Setup.exe'
        if (Test-Path -LiteralPath $latestSetup) { Remove-Item -LiteralPath $latestSetup -Force }
        try {
            New-Item -ItemType HardLink -Path $latestSetup -Target $setupExe -ErrorAction Stop | Out-Null
        } catch {
            Copy-Item -LiteralPath $setupExe -Destination $latestSetup -Force
        }

        $sizeMb = [math]::Round((Get-Item -LiteralPath $setupExe).Length / 1MB, 1)
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
