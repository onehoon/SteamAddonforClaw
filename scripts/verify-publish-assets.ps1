[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublishDirectory,

    [switch]$RequireFsePackage,

    [string]$ExpectedFsePackageVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-SdkTool([string]$name) {
    $command = Get-Command "$name.exe" -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
    ) | Where-Object { $_ -and (Test-Path $_) }
    $tool = Get-ChildItem -Path $roots -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($tool) { return $tool.FullName }
    throw "$name.exe was not found in the Windows SDK."
}

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
    'fse\SteamInputAddonforClaw.FseHome.msix',
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

if ($RequireFsePackage) {
    $fsePackagePath = Join-Path $PublishDirectory 'fse\SteamInputAddonforClaw.FseHome.msix'
    if (-not (Test-Path -LiteralPath $fsePackagePath -PathType Leaf)) {
        throw "Final signed FSE Home package is missing: $fsePackagePath"
    }

    $makeAppx = Find-SdkTool 'makeappx'
    $signTool = Find-SdkTool 'signtool'
    $unpackDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("SteamInputAddonforClaw-fse-verify-" + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $unpackDirectory -Force | Out-Null
        & $makeAppx unpack /p $fsePackagePath /d $unpackDirectory /o
        if ($LASTEXITCODE -ne 0) { throw "Unable to unpack final FSE Home package with exit code $LASTEXITCODE." }

        & $signTool verify /pa /all /v $fsePackagePath
        if ($LASTEXITCODE -ne 0) { throw "Final FSE Home package signature verification failed with exit code $LASTEXITCODE." }

        $finalManifestPath = Join-Path $unpackDirectory 'AppxManifest.xml'
        $finalManifest = Get-Content -LiteralPath $finalManifestPath -Raw
        $sourceManifest = Get-Content -LiteralPath (Join-Path $PublishDirectory 'fse\Package\AppxManifest.xml') -Raw
        foreach ($requiredText in @('Name="SteamInputAddonforClaw.FseHome"', 'Id="App"', 'windows.gamingApp', 'Microsoft.appCategory.gamingHome_8wekyb3d8bbwe')) {
            if ($finalManifest -notmatch [regex]::Escape($requiredText)) {
                throw "Final FSE Home package manifest is missing required content: $requiredText"
            }
        }

        $sourceXml = [System.Xml.Linq.XDocument]::Parse($sourceManifest)
        $finalXml = [System.Xml.Linq.XDocument]::Parse($finalManifest)
        $sourceIdentity = $sourceXml.Root.Elements() | Where-Object { $_.Name.LocalName -eq 'Identity' } | Select-Object -First 1
        $finalIdentity = $finalXml.Root.Elements() | Where-Object { $_.Name.LocalName -eq 'Identity' } | Select-Object -First 1
        if ($null -eq $sourceIdentity -or $null -eq $finalIdentity) { throw 'FSE Home package identity could not be parsed.' }
        foreach ($attribute in @('Name', 'Publisher', 'ProcessorArchitecture')) {
            $sourceAttribute = $sourceIdentity.Attribute($attribute)
            $finalAttribute = $finalIdentity.Attribute($attribute)
            $sourceValue = if ($null -eq $sourceAttribute) { $null } else { $sourceAttribute.Value }
            $finalValue = if ($null -eq $finalAttribute) { $null } else { $finalAttribute.Value }
            if ($sourceValue -ne $finalValue) {
                throw "Final FSE Home package identity attribute '$attribute' does not match the source manifest."
            }
        }
        $finalVersionAttribute = $finalIdentity.Attribute('Version')
        $finalVersion = if ($null -eq $finalVersionAttribute) { $null } else { $finalVersionAttribute.Value }
        if ($ExpectedFsePackageVersion -and $finalVersion -ne $ExpectedFsePackageVersion) {
            throw "Final FSE Home package version '$finalVersion' does not match expected version '$ExpectedFsePackageVersion'."
        }
        if (-not (Test-Path -LiteralPath (Join-Path $unpackDirectory 'CustomCapability.SCCD') -PathType Leaf)) {
            throw 'Final FSE Home package does not contain CustomCapability.SCCD.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $unpackDirectory) {
            Remove-Item -LiteralPath $unpackDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
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
