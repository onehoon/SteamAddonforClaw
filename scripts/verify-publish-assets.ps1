[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Xml.Linq

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish directory was not found: $PublishDirectory"
}

$requiredAssets = @(
    'SteamInputAddonforClaw.exe',
    'SteamInputAddonforClaw.TdpHelper.exe',
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
    'fse\SteamInputAddonforClaw.FseHome.cer'
)

$missingAssets = foreach ($asset in $requiredAssets) {
    $assetPath = Join-Path $PublishDirectory $asset
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        $asset
    }
}

if ($missingAssets) {
    throw "Publish output is missing required Runtime assets: $($missingAssets -join ', ')"
}

foreach ($forbiddenPrerequisiteInstaller in @{
    'Dependencies\HidHide\HidHide_1.5.230_x64.exe' = 'HidHide'
    'Dependencies\UsbIpWin2\USBip-0.9.8.0-x64.exe' = 'usbip-win2'
}.GetEnumerator()) {
    if (Test-Path -LiteralPath (Join-Path $PublishDirectory $forbiddenPrerequisiteInstaller.Key) -PathType Leaf) {
        throw "Published output must not bundle the $($forbiddenPrerequisiteInstaller.Value) prerequisite installer."
    }
}

$viiperPayload = Join-Path $PublishDirectory 'Dependencies\Viiper\libVIIPER.dll'
# Must match the vendored VIIPER DLL recorded in viiper.lock.json and PROVENANCE.md.
$expectedViiperSha256 = '5F2CE963B8ADA1FDE78BF4A1C25BF063503D761E3D42DA6BB418FE735CE1F948'
if ((Get-FileHash -LiteralPath $viiperPayload -Algorithm SHA256).Hash -ne $expectedViiperSha256) {
    throw 'Published VIIPER payload SHA-256 does not match its recorded provenance.'
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

$fsePackagePath = Join-Path $PublishDirectory 'fse\SteamInputAddonforClaw.FseHome.msix'
$fseCertificatePath = Join-Path $PublishDirectory 'fse\SteamInputAddonforClaw.FseHome.cer'
$expectedFsePackageSha256 = '9D4C46ABCC1324803AE5AB031B11EC8EF39057D77C9C04FCB243D80BC122F86B'
$expectedFseCertificateSha256 = '663053482DA50F9017CC902CA5DF6E9BBFD5A6F06624B8608266318F54687390'
if ((Get-FileHash -LiteralPath $fsePackagePath -Algorithm SHA256).Hash -ne $expectedFsePackageSha256) {
    throw 'Published FSE MSIX SHA-256 does not match the fixed distribution artifact.'
}
if ((Get-FileHash -LiteralPath $fseCertificatePath -Algorithm SHA256).Hash -ne $expectedFseCertificateSha256) {
    throw 'Published FSE public certificate SHA-256 does not match the fixed distribution artifact.'
}

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($fseCertificatePath)
if ($certificate.Subject -ne 'CN=SteamInputAddonforClaw') {
    throw "Published FSE certificate subject '$($certificate.Subject)' does not match the package publisher."
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($fsePackagePath)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    $sccdEntry = $archive.GetEntry('CustomCapability.SCCD')
    if ($null -eq $manifestEntry) { throw 'Fixed FSE MSIX does not contain AppxManifest.xml.' }
    if ($null -eq $sccdEntry) { throw 'Fixed FSE MSIX does not contain CustomCapability.SCCD.' }

    $manifestReader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try { $fseManifest = $manifestReader.ReadToEnd() }
    finally { $manifestReader.Dispose() }
    $sccdReader = [System.IO.StreamReader]::new($sccdEntry.Open())
    try { $sccd = $sccdReader.ReadToEnd() }
    finally { $sccdReader.Dispose() }

    $manifestXml = [System.Xml.Linq.XDocument]::Parse($fseManifest)
    $identity = $manifestXml.Root.Elements() | Where-Object { $_.Name.LocalName -eq 'Identity' } | Select-Object -First 1
    if ($null -eq $identity) { throw 'Fixed FSE MSIX manifest has no Identity element.' }
    $identityValues = @{
        Name = $identity.Attribute('Name').Value
        Publisher = $identity.Attribute('Publisher').Value
        Version = $identity.Attribute('Version').Value
        ProcessorArchitecture = $identity.Attribute('ProcessorArchitecture').Value
    }
    if ($identityValues.Name -ne 'SteamInputAddonforClaw.FseHome' -or
        $identityValues.Publisher -ne 'CN=SteamInputAddonforClaw' -or
        $identityValues.Version -ne '1.0.0.0' -or
        $identityValues.ProcessorArchitecture -ne 'x64') {
        throw 'Fixed FSE MSIX manifest identity does not match the pinned package contract.'
    }
    $application = $manifestXml.Descendants() | Where-Object {
        $_.Name.LocalName -eq 'Application' -and $null -ne $_.Attribute('Id') -and $_.Attribute('Id').Value -eq 'App'
    } | Select-Object -First 1
    if ($null -eq $application) { throw 'Fixed FSE MSIX manifest is missing Application Id App.' }
    foreach ($requiredText in @('windows.gamingApp', 'Microsoft.appCategory.gamingHome_8wekyb3d8bbwe')) {
        if ($fseManifest -notmatch [regex]::Escape($requiredText)) {
            throw "Fixed FSE MSIX manifest is missing required content: $requiredText"
        }
    }
    if ($sccd -notmatch 'Microsoft\.appCategory\.gamingHome_8wekyb3d8bbwe') {
        throw 'Fixed FSE MSIX SCCD is missing the Gaming Home custom capability.'
    }
}
finally {
    $archive.Dispose()
    $certificate.Dispose()
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
