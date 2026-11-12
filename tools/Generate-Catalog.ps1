#Requires -Version 5.1
<#
.SYNOPSIS
    Regenerates the typed tweak catalog from a clone of the 0.2.0 Electron repository.
.DESCRIPTION
    The generated catalog, plans and shortcuts are committed under
    src/AtomToolbox/Atom.Toolbox.Infrastructure/Catalog, so a normal build does not need
    the Electron source. Pass -LegacyRoot (or set ATOM_LEGACY_ROOT) only when regenerating.
#>
[CmdletBinding()]
param(
    [string]$LegacyRoot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $LegacyRoot) {
    $LegacyRoot = $env:ATOM_LEGACY_ROOT
}
if (-not $LegacyRoot) {
    $LegacyRoot = Join-Path (Split-Path -Parent $root) 'AtomToolBox-legacy'
}

if (-not (Test-Path -LiteralPath $LegacyRoot)) {
    throw "Legacy source not found: $LegacyRoot. Clone https://github.com/osama5alifa/AtomToolBox and pass -LegacyRoot."
}

$project = Join-Path $root 'tools\Atom.CatalogBuilder\Atom.CatalogBuilder.csproj'
& dotnet run --project $project -c Release -- --root $root --legacy $LegacyRoot
if ($LASTEXITCODE -ne 0) {
    throw "Catalog generation failed with exit code $LASTEXITCODE"
}

Write-Host 'Catalog regenerated. Review tools\catalog-report.txt and rebuild.' -ForegroundColor Green
