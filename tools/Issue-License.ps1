#Requires -Version 5.1
<#
.SYNOPSIS
    Issues an offline AtomToolBox PRO licence token.
.EXAMPLE
    .\Issue-License.ps1 -Licensee "Jane Doe" -Expires 2027-12-31
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Licensee,
    [string]$Tier = 'pro',
    [string]$Expires = (Get-Date).AddYears(1).ToString('yyyy-MM-dd'),
    [string]$PrivateKey = (Join-Path $PSScriptRoot 'keys\atom-license-private.pem')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools\Atom.LicenseIssuer\Atom.LicenseIssuer.csproj'

if (-not (Test-Path -LiteralPath $PrivateKey)) {
    throw "Private key not found: $PrivateKey. Generate one with: dotnet run --project tools/Atom.LicenseIssuer -- generate"
}

& dotnet run --project $project -c Release -- issue --private $PrivateKey --licensee $Licensee --tier $Tier --expires $Expires
if ($LASTEXITCODE -ne 0) {
    throw "Licence issuance failed with exit code $LASTEXITCODE"
}
