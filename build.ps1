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
        $payloadZip = Join-Path $root 'publish\payload.zip'
        $setupExe = Join-Path $root 'publish\AtomToolbox-Setup.exe'
        $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

        Write-Host 'Restoring the self-contained win-x64 runtime...' -ForegroundColor Cyan
        Invoke-DotNet (@('restore', $project, '-r', 'win-x64', '-p:RestoreIgnoreFailedSources=true') + $common)

        Write-Host 'Publishing the self-contained application...' -ForegroundColor Cyan
        if (Test-Path -LiteralPath $selfContained) { Remove-Item -LiteralPath $selfContained -Recurse -Force }
        Invoke-DotNet (@('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false',
            '-p:DebugType=None', '-p:DebugSymbols=false', '--no-restore', '-o', $selfContained) + $common)

        $appExe = Join-Path $selfContained 'AtomToolbox.exe'
        if (-not (Test-Path -LiteralPath $appExe)) { throw 'Self-contained publish did not produce AtomToolbox.exe.' }
        $bytes = [IO.File]::ReadAllBytes($appExe)
        $latin = [Text.Encoding]::GetEncoding(1252).GetString($bytes)
        if ($latin.IndexOf('requireAdministrator') -lt 0) {
            throw 'The published application does not request administrator rights.'
        }

        $sevenZip = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
        if (-not (Test-Path -LiteralPath $sevenZip)) { $sevenZip = Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe' }
        if (-not (Test-Path -LiteralPath $sevenZip)) { throw '7-Zip was not found.' }

        if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip -Force }
        Push-Location $selfContained
        try {
            & $sevenZip a -tzip -mx=9 $payloadZip '*'
            if ($LASTEXITCODE -ne 0) { throw "7-Zip exited with code $LASTEXITCODE" }
        } finally {
            Pop-Location
        }

        Get-Process -Name AtomToolbox-Setup -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $setupExe) { Remove-Item -LiteralPath $setupExe -Force }

        Write-Host 'Compiling the standalone installer...' -ForegroundColor Cyan
        & $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$setupExe" `
            "/win32icon:$root\src\Atomtoolbox\Assets\app.ico" `
            "/win32manifest:$root\installer\AtomToolboxSetup.manifest" `
            "/r:$fw\System.dll" "/r:$fw\System.Core.dll" "/r:$fw\System.Drawing.dll" `
            "/r:$fw\System.Windows.Forms.dll" "/r:$fw\System.IO.Compression.dll" `
            "/r:$fw\System.IO.Compression.FileSystem.dll" `
            "/resource:$payloadZip,payload.zip" `
            "$root\installer\AtomToolboxSetup.cs"
        if ($LASTEXITCODE -ne 0) { throw "csc exited with code $LASTEXITCODE" }
        if (-not (Test-Path -LiteralPath $setupExe)) { throw 'The installer was not created.' }

        Remove-Item -LiteralPath $selfContained -Recurse -Force
        Remove-Item -LiteralPath $payloadZip -Force
        $sizeMb = [math]::Round((Get-Item -LiteralPath $setupExe).Length / 1MB, 1)
        Write-Host "Installer ready: $setupExe ($sizeMb MB)" -ForegroundColor Green
        Write-Host 'One file. No temp extraction and no .NET runtime prerequisite.' -ForegroundColor Green
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
