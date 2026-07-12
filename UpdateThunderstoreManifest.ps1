# Updates Thunderstore package manifest version_number.

param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Normalize-PackageVersion {
    param([Parameter(Mandatory = $true)][string]$Value)

    $Trimmed = $Value.Trim()
    if ($Trimmed -match '^(\d+)\.(\d+)\.(\d+)(?:\.\d+)?(?:[-+].*)?$') {
        return "$($Matches[1]).$($Matches[2]).$($Matches[3])"
    }

    throw "Thunderstore version_number must resolve to MAJOR.MINOR.PATCH. Received: $Value"
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Thunderstore manifest was not found: $ManifestPath"
}

$PackageVersion = Normalize-PackageVersion -Value $Version
$Manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$OldVersion = [string]$Manifest.version_number
$Manifest.version_number = $PackageVersion
$Manifest | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8

if ($OldVersion -eq $PackageVersion) {
    Write-Host "Thunderstore manifest version is already $PackageVersion"
}
else {
    Write-Host "Thunderstore manifest version updated: $OldVersion -> $PackageVersion"
}
