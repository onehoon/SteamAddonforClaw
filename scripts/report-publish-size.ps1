[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$PublishDirectory,
    [ValidateRange(1, 100)] [int]$TopCount = 20,
    [string]$JsonOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Publish directory was not found: $root" }

function Get-Component([string]$Path) {
    $path = $Path.ToLowerInvariant()
    if ($path.StartsWith('ui/')) { return 'UI' }
    if ($path.StartsWith('qam/')) { return 'QAM Host' }
    if ($path.StartsWith('centermhelpersource/')) { return 'CenterM Helper' }
    if ($path.StartsWith('dependencies/hidhide/')) { return 'HidHide' }
    if ($path.StartsWith('dependencies/usbipwin2/')) { return 'USBip-win2' }
    if ($path.StartsWith('dependencies/viiper/')) { return 'VIIPER' }
    if ($path -eq 'steaminputaddonforclaw.tdphelper.exe' -or $path -match '^steaminputaddonforclaw\.tdphelper\.') { return 'TDP Helper' }
    if ($path -notmatch '/') { return 'Runtime' }
    return 'Other / Unclassified'
}

function Format-MiB([long]$Bytes) { return '{0:N2}' -f ($Bytes / 1MB) }

$files = @(Get-ChildItem -LiteralPath $root -Recurse -File)
$totalBytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
$componentBytes = [ordered]@{
    'Runtime' = [long]0; 'UI' = [long]0; 'QAM Host' = [long]0; 'TDP Helper' = [long]0;
    'CenterM Helper' = [long]0; 'HidHide' = [long]0; 'USBip-win2' = [long]0; 'VIIPER' = [long]0;
    'Other / Unclassified' = [long]0
}
$fileComponents = foreach ($file in $files) {
    $relative = ($file.FullName.Substring($root.Length).TrimStart('\', '/')).Replace('\', '/')
    $component = Get-Component $relative
    $componentBytes[$component] += [long]$file.Length
    [pscustomobject]@{ Path = $relative; Bytes = [long]$file.Length; Component = $component }
}
$classifiedBytes = [long](($componentBytes.GetEnumerator() | Where-Object Key -ne 'Other / Unclassified' | Measure-Object -Property Value -Sum).Sum)
$unclassifiedBytes = [long]$componentBytes['Other / Unclassified']
if ($totalBytes - $classifiedBytes -ne $unclassifiedBytes) { throw "Publish size classification mismatch: total=$totalBytes classified=$classifiedBytes unclassified=$unclassifiedBytes." }

$rows = foreach ($entry in $componentBytes.GetEnumerator()) {
    [pscustomobject][ordered]@{ Name = $entry.Key; Bytes = [long]$entry.Value; MiB = [double]($entry.Value / 1MB); Percent = if ($totalBytes) { [double]($entry.Value * 100 / $totalBytes) } else { 0.0 } }
}
$thirdPartyBytes = [long]($componentBytes['HidHide'] + $componentBytes['USBip-win2'] + $componentBytes['VIIPER'])
$largestFiles = @($fileComponents | Sort-Object Bytes -Descending | Select-Object -First $TopCount | ForEach-Object {
    [pscustomobject][ordered]@{ Path = $_.Path; Bytes = $_.Bytes; MiB = [double]($_.Bytes / 1MB); Component = $_.Component }
})

Write-Host 'Steam Input Addon for Claw - Publish Size Report'
Write-Host 'Units: MiB = bytes / 1024 / 1024'
Write-Host ''
Write-Host ('Total  {0:N0} bytes ({1} MiB)' -f $totalBytes, (Format-MiB $totalBytes))
Write-Host 'Components'
foreach ($row in $rows) { Write-Host ('  {0,-20} {1,15:N0} bytes  {2,9} MiB  {3,6:N2}%' -f $row.Name, $row.Bytes, (Format-MiB $row.Bytes), $row.Percent) }
Write-Host ('  {0,-20} {1,15:N0} bytes' -f 'Unclassified check', $unclassifiedBytes)
Write-Host ('  {0,-20} {1,15:N0} bytes  {2,9} MiB' -f 'Third-party total', $thirdPartyBytes, (Format-MiB $thirdPartyBytes))
Write-Host "Largest files (Top $TopCount)"
foreach ($file in $largestFiles) { Write-Host ('  {0,15:N0} bytes  {1,9} MiB  {2}' -f $file.Bytes, (Format-MiB $file.Bytes), $file.Path) }

if ($JsonOutputPath) {
    $jsonPath = [System.IO.Path]::GetFullPath($JsonOutputPath)
    $parent = Split-Path -Parent $jsonPath
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $json = [ordered]@{ publishDirectory = $root; totalBytes = $totalBytes; thirdPartyBytes = $thirdPartyBytes; unclassifiedBytes = $unclassifiedBytes; components = [ordered]@{}; largestFiles = $largestFiles }
    foreach ($row in $rows) { $json.components[$row.Name] = $row }
    $json | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding utf8
}
