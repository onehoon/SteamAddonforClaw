[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\report-publish-size.ps1'
$root = Join-Path ([System.IO.Path]::GetTempPath()) "SteamInputAddonforClaw.PublishSize.Tests.$([Guid]::NewGuid().ToString('N'))"
$jsonPath = Join-Path $root 'report.json'
function Write-TestFile([string]$RelativePath, [int]$Length) {
    $path = Join-Path $root $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [System.IO.File]::WriteAllBytes($path, [byte[]]::new($Length))
}
try {
    Write-TestFile 'SteamInputAddonforClaw.exe' 10; Write-TestFile 'misc\unclassified.bin' 3
    Write-TestFile 'ui\managed.dll' 20
    Write-TestFile 'fse\SteamInputAddonforClaw.FseHome.msix' 7; Write-TestFile 'fse\SteamInputAddonforClaw.FseHome.cer' 2
    Write-TestFile 'SteamInputAddonforClaw.TdpHelper.exe' 5
    Write-TestFile 'Dependencies\HidHide\installer.exe' 11; Write-TestFile 'Dependencies\UsbIpWin2\installer.exe' 13
    Write-TestFile 'Dependencies\Viiper\libVIIPER.dll' 17
    & $scriptPath -PublishDirectory $root -TopCount 3 -JsonOutputPath $jsonPath | Out-Null
    $report = Get-Content -Raw $jsonPath | ConvertFrom-Json
    if ($report.totalBytes -ne 88) { throw "Expected 88 total bytes, got $($report.totalBytes)." }
    if ($report.unclassifiedBytes -ne 3 -or $report.components.'Other / Unclassified'.bytes -ne 3) { throw 'Expected three unclassified bytes.' }
    if ($report.components.Runtime.bytes -ne 10 -or $report.components.UI.bytes -ne 20 -or $report.components.Overlay.bytes -ne 0) { throw 'Runtime/UI classification failed.' }
    if ($report.components.'TDP Helper'.bytes -ne 5) { throw 'Helper classification failed.' }
    if ($report.components.'FSE Home'.bytes -ne 9) { throw 'FSE Home classification failed.' }
    if ($report.components.HidHide.bytes -ne 11 -or $report.components.'USBip-win2'.bytes -ne 13 -or $report.components.VIIPER.bytes -ne 17) { throw 'Third-party classification failed.' }
    if ($report.largestFiles[0].Path -ne 'ui/managed.dll' -or $report.largestFiles.Count -ne 3) { throw 'Largest-file ordering failed.' }
    Write-Host 'Publish size report tests passed.'
}
finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
