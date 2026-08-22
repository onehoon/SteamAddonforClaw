[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish directory was not found: $PublishDirectory"
}

$requiredAssets = @(
    'SteamInputAddonforClaw.exe',
    'SteamInputAddonforClaw.TdpHelper.exe',
    'CenterMHelperSource\CenterMHelper.exe',
    'Dependencies\HidHide\HidHide_1.5.230_x64.exe',
    'Dependencies\UsbIpWin2\USBip-0.9.7.7-x64.exe',
    'Dependencies\Viiper\libVIIPER.dll',
    'Dependencies\Viiper\PROVENANCE.md',
    'Dependencies\Viiper\libVIIPER.h',
    'Dependencies\Viiper\LICENSE.txt',
    'ui\SteamInputAddonforClaw.UI.exe',
    'ui\SteamInputAddonforClaw.UI.dll',
    'ui\SteamInputAddonforClaw.UI.pri',
    'ui\App.xbf',
    'ui\MainWindow.xbf',
    'ui\Views\StatusPage.xbf',
    'ui\Views\HowToUsePage.xbf',
    'ui\Views\ControllerPage.xbf',
    'ui\Views\SettingsPage.xbf',
    'ui\Views\DeveloperPage.xbf',
    'qam\SteamInputAddonforClaw.QamHost.exe',
    'qam\Frontend\qam.js'
)

$missingAssets = foreach ($asset in $requiredAssets) {
    $assetPath = Join-Path $PublishDirectory $asset
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        $asset
    }
}

$hidHideInstaller = Join-Path $PublishDirectory 'Dependencies\HidHide\HidHide_1.5.230_x64.exe'
$expectedHidHideSha256 = 'F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6'
if ((Get-FileHash -LiteralPath $hidHideInstaller -Algorithm SHA256).Hash -ne $expectedHidHideSha256) {
    throw 'Published HidHide installer SHA-256 does not match the bundled metadata.'
}

$usbIpInstaller = Join-Path $PublishDirectory 'Dependencies\UsbIpWin2\USBip-0.9.7.7-x64.exe'
$expectedUsbIpSha256 = '51620FA5F9F8BE5932BC9D786DEEE557CE06D5407A99CAB490DCFAC71F185FEA'
if ((Get-FileHash -LiteralPath $usbIpInstaller -Algorithm SHA256).Hash -ne $expectedUsbIpSha256) {
    throw 'Published USB/IP installer SHA-256 does not match the bundled metadata.'
}

$viiperPayload = Join-Path $PublishDirectory 'Dependencies\Viiper\libVIIPER.dll'
# Must match the vendored VIIPER DLL recorded in viiper.lock.json and PROVENANCE.md.
$expectedViiperSha256 = 'D07D2E5A622983AED6B9CC676B59B5B3A31A2B343015C4492FA5BDAE74DD0CB6'
if ((Get-FileHash -LiteralPath $viiperPayload -Algorithm SHA256).Hash -ne $expectedViiperSha256) {
    throw 'Published VIIPER payload SHA-256 does not match its recorded provenance.'
}

if ($missingAssets) {
    throw "Publish output is missing required Runtime assets: $($missingAssets -join ', ')"
}

$centerMHelperSource = Join-Path $PublishDirectory 'CenterMHelperSource'
$centerMHelperSidecars = @(Get-ChildItem -LiteralPath $centerMHelperSource -File | Where-Object {
    $_.Name -match '\.(dll|deps\.json|runtimeconfig\.json)$'
})
if ($centerMHelperSidecars.Count -gt 0) {
    throw "CenterMHelperSource contains NativeAOT-forbidden sidecar payload: $($centerMHelperSidecars.Name -join ', ')"
}

$uiDirectory = Join-Path $PublishDirectory 'ui'
$uiManagedPayload = @(Get-ChildItem -LiteralPath $uiDirectory -Recurse -File -Filter '*.dll')
$uiPriPayload = @(Get-ChildItem -LiteralPath $uiDirectory -Recurse -File -Filter '*.pri')
$uiWinmdPayload = @(Get-ChildItem -LiteralPath $uiDirectory -Recurse -File -Filter '*.winmd')
if ($uiManagedPayload.Count -eq 0 -or $uiPriPayload.Count -eq 0 -or $uiWinmdPayload.Count -eq 0) {
    throw 'UI publish output is missing its self-contained managed or WinUI/Windows App SDK payload.'
}

$applicationPri = Join-Path $uiDirectory 'SteamInputAddonforClaw.UI.pri'
if (-not (Test-Path -LiteralPath $applicationPri -PathType Leaf)) {
    throw 'UI publish output is missing the external UI application PRI: SteamInputAddonforClaw.UI.pri'
}

$runtimeRootXaml = @(Get-ChildItem -LiteralPath $PublishDirectory -File | Where-Object { $_.Extension -in @('.xbf', '.pri') })
if ($runtimeRootXaml.Count -gt 0) {
    throw "Runtime publish root contains UI assets: $($runtimeRootXaml.Name -join ', ')"
}

Write-Host 'Published Runtime assets verified.'
