[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "FSE Home package was not found: $PackagePath"
}

try {
    Add-AppxPackage -Path (Resolve-Path -LiteralPath $PackagePath).Path -ForceApplicationShutdown -ErrorAction Stop
    $package = @(Get-AppxPackage -Name 'SteamInputAddonforClaw.FseHome' -ErrorAction Stop)
    if ($package.Count -ne 1) {
        throw "Expected one registered SteamInputAddonforClaw.FseHome package, found $($package.Count)."
    }
    $aumid = "$($package[0].PackageFamilyName)!App"
    Write-Host "FSE Home package registered. PackageFamilyName=$($package[0].PackageFamilyName) AUMID=$aumid"
    Write-Host 'The Settings toggle can now select it without reinstalling the package.'
}
catch {
    throw "FSE Home package registration failed. Use a package signed by the publisher certificate and ensure the Windows Gaming FSE capability requirements are satisfied. $($_.Exception.Message)"
}
