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
    'Dependencies\HidHide\HidHide_1.5.230_x64.exe',
    'Dependencies\UsbIpWin2\USBip-0.9.8.0-x64.exe',
    'Dependencies\Viiper\libVIIPER.dll',
    'Dependencies\Viiper\PROVENANCE.md',
    'Dependencies\Viiper\libVIIPER.h',
    'Dependencies\Viiper\LICENSE.txt',
    'Dependencies\ClawHUD\clawhud.lock.json',
    'ui\SteamInputAddonforClaw.UI.exe',
    'ui\SteamInputAddonforClaw.UI.dll',
    'ui\SteamInputAddonforClaw.UI.pri',
    'ui\App.xbf',
    'ui\MainWindow.xbf',
    'ui\Views\HowToUsePage.xbf',
    'ui\Views\ControllerPage.xbf',
    'ui\Views\SettingsPage.xbf',
    'ui\Views\DeveloperPage.xbf',
    'qam\SteamInputAddonforClaw.QamHost.exe',
    'qam\Frontend\qam.js',
    'overlay\SteamInputAddonforClaw.Overlay.exe',
    'overlay\SteamInputAddonforClaw.Overlay.pri',
    'overlay\App.xbf',
    'overlay\OverlayWindow.xbf',
    'fse\SteamInputAddonforClaw.FseHome.exe',
    'fse\SteamInputAddonforClaw.FseHome.dll',
    'fse\Package\AppxManifest.xml',
    'fse\Package\CustomCapability.SCCD',
    'fse\Package\Assets\AppIcon.ico',
    'fse\Package\Public\README.txt'
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

$usbIpInstaller = Join-Path $PublishDirectory 'Dependencies\UsbIpWin2\USBip-0.9.8.0-x64.exe'
$expectedUsbIpSha256 = '81F426741F7EE2ED991FEBE24A22DACA8400B6AE2F171054E3FB404897E15D39'
if ((Get-FileHash -LiteralPath $usbIpInstaller -Algorithm SHA256).Hash -ne $expectedUsbIpSha256) {
    throw 'Published USB/IP installer SHA-256 does not match the bundled metadata.'
}

$viiperPayload = Join-Path $PublishDirectory 'Dependencies\Viiper\libVIIPER.dll'
# Must match the vendored VIIPER DLL recorded in viiper.lock.json and PROVENANCE.md.
$expectedViiperSha256 = '5F2CE963B8ADA1FDE78BF4A1C25BF063503D761E3D42DA6BB418FE735CE1F948'
if ((Get-FileHash -LiteralPath $viiperPayload -Algorithm SHA256).Hash -ne $expectedViiperSha256) {
    throw 'Published VIIPER payload SHA-256 does not match its recorded provenance.'
}

if ($missingAssets) {
    throw "Publish output is missing required Runtime assets: $($missingAssets -join ', ')"
}

$clawHudLockPath = Join-Path $PublishDirectory 'Dependencies\ClawHUD\clawhud.lock.json'
$clawHudLock = Get-Content -LiteralPath $clawHudLockPath -Raw | ConvertFrom-Json
if ($clawHudLock.schema_version -ne 1) { throw 'Published ClawHUD lock schema must be 1.' }
if ([string]$clawHudLock.runtime_version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Published ClawHUD lock runtime_version is invalid.' }
if ([string]$clawHudLock.tag -ne "steamaddon-runtime-v$($clawHudLock.runtime_version)") { throw 'Published ClawHUD lock tag does not match runtime_version.' }
if ([string]$clawHudLock.asset -ne 'ClawHUDRuntime.zip') { throw 'Published ClawHUD lock asset is invalid.' }
if ([string]$clawHudLock.source_commit -notmatch '^[0-9a-fA-F]{40}$') { throw 'Published ClawHUD lock source_commit is invalid.' }
if ([string]$clawHudLock.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Published ClawHUD lock sha256 is invalid.' }

$clawHudDirectory = Join-Path $PublishDirectory 'Dependencies\ClawHUD'
foreach ($forbiddenClawHudAsset in @('ClawHUDRuntime.zip', 'ClawHUD.exe')) {
    if (Test-Path -LiteralPath (Join-Path $clawHudDirectory $forbiddenClawHudAsset) -PathType Leaf) {
        throw "Published output must not bundle ClawHUD Runtime payload: $forbiddenClawHudAsset"
    }
}

$qamSdkProjection = Join-Path $PublishDirectory 'qam\Microsoft.Windows.SDK.NET.dll'
if (Test-Path -LiteralPath $qamSdkProjection -PathType Leaf) {
    throw 'QAM publish output must not contain Microsoft.Windows.SDK.NET.dll.'
}

$runtimePayloadNames = @('System.Private.CoreLib.dll', 'coreclr.dll', 'hostpolicy.dll')
foreach ($directory in @($PublishDirectory, (Join-Path $PublishDirectory 'ui'), (Join-Path $PublishDirectory 'qam'), (Join-Path $PublishDirectory 'overlay'), (Join-Path $PublishDirectory 'fse'))) {
    $runtimePayload = @(Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -in $runtimePayloadNames })
    if ($runtimePayload.Count -gt 0) {
        throw "Framework-dependent publish contains runtime payload in '$directory': $($runtimePayload.Name -join ', ')"
    }
}

$fseManifest = Get-Content -LiteralPath (Join-Path $PublishDirectory 'fse\Package\AppxManifest.xml') -Raw
foreach ($requiredText in @('windows.gamingApp', 'Microsoft.appCategory.gamingHome_8wekyb3d8bbwe', 'SteamInputAddonforClaw.FseHome', 'Id="App"')) {
    if ($fseManifest -notmatch [regex]::Escape($requiredText)) {
        throw "FSE Home package manifest is missing required content: $requiredText"
    }
}
$sccd = Get-Content -LiteralPath (Join-Path $PublishDirectory 'fse\Package\CustomCapability.SCCD') -Raw
if ($sccd -notmatch 'Microsoft\.appCategory\.gamingHome_8wekyb3d8bbwe') {
    throw 'FSE Home package SCCD is missing the Gaming Home custom capability.'
}

$uiDirectory = Join-Path $PublishDirectory 'ui'
$uiManagedPayload = @(Get-ChildItem -LiteralPath $uiDirectory -Recurse -File -Filter '*.dll')
$uiPriPayload = @(Get-ChildItem -LiteralPath $uiDirectory -Recurse -File -Filter '*.pri')
$uiWinmdPayload = @(Get-ChildItem -LiteralPath $uiDirectory -Recurse -File -Filter '*.winmd')
if ($uiManagedPayload.Count -eq 0 -or $uiPriPayload.Count -eq 0 -or $uiWinmdPayload.Count -eq 0) {
    throw 'UI publish output is missing its framework-dependent managed or WinUI/Windows App SDK payload.'
}

$applicationPri = Join-Path $uiDirectory 'SteamInputAddonforClaw.UI.pri'
if (-not (Test-Path -LiteralPath $applicationPri -PathType Leaf)) {
    throw 'UI publish output is missing the external UI application PRI: SteamInputAddonforClaw.UI.pri'
}

$overlayDirectory = Join-Path $PublishDirectory 'overlay'
$overlayManagedPayload = @(Get-ChildItem -LiteralPath $overlayDirectory -Recurse -File -Filter '*.dll')
$overlayPriPayload = @(Get-ChildItem -LiteralPath $overlayDirectory -Recurse -File -Filter '*.pri')
$overlayWinmdPayload = @(Get-ChildItem -LiteralPath $overlayDirectory -Recurse -File -Filter '*.winmd')
if ($overlayManagedPayload.Count -eq 0 -or $overlayPriPayload.Count -eq 0 -or $overlayWinmdPayload.Count -eq 0) {
    throw 'Overlay publish output is missing its framework-dependent managed or WinUI/Windows App SDK payload.'
}

$runtimeRootXaml = @(Get-ChildItem -LiteralPath $PublishDirectory -File | Where-Object { $_.Extension -in @('.xbf', '.pri') })
if ($runtimeRootXaml.Count -gt 0) {
    throw "Runtime publish root contains UI assets: $($runtimeRootXaml.Name -join ', ')"
}

Write-Host 'Published Runtime assets verified.'
