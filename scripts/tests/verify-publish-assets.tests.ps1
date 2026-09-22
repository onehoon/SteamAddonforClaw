$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $repoRoot 'scripts\verify-publish-assets.ps1'
$dependencyRoot = Join-Path $repoRoot 'src\SteamInputAddonforClaw\Dependencies'

function New-Fixture {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("publish-assets-verify-test-" + [System.Guid]::NewGuid())
    $directories = @(
        $root,
        (Join-Path $root 'Dependencies\HidHide'),
        (Join-Path $root 'Dependencies\UsbIpWin2'),
        (Join-Path $root 'Dependencies\Viiper'),
        (Join-Path $root 'Dependencies\ClawHUD'),
        (Join-Path $root 'ui\Views'),
        (Join-Path $root 'qam\Frontend'),
        (Join-Path $root 'overlay'),
        (Join-Path $root 'fse')
    )
    New-Item -ItemType Directory -Force -Path $directories | Out-Null

    $files = @{
        'SteamInputAddonforClaw.exe' = 'runtime'
        'SteamInputAddonforClaw.TdpHelper.exe' = 'tdp helper'
        'Dependencies\Viiper\libVIIPER.dll' = (Join-Path $dependencyRoot 'Viiper\libVIIPER.dll')
        'Dependencies\Viiper\PROVENANCE.md' = (Join-Path $dependencyRoot 'Viiper\PROVENANCE.md')
        'Dependencies\Viiper\libVIIPER.h' = (Join-Path $dependencyRoot 'Viiper\libVIIPER.h')
        'Dependencies\Viiper\LICENSE.txt' = (Join-Path $dependencyRoot 'Viiper\LICENSE.txt')
        'Dependencies\ClawHUD\clawhud.lock.json' = (Join-Path $dependencyRoot 'ClawHUD\clawhud.lock.json')
        'ui\SteamInputAddonforClaw.UI.exe' = 'ui executable'
        'ui\SteamInputAddonforClaw.UI.dll' = 'managed payload'
        'ui\SteamInputAddonforClaw.UI.pri' = 'application pri'
        'ui\App.xbf' = 'app xbf'
        'ui\MainWindow.xbf' = 'main window xbf'
        'ui\Views\HowToUsePage.xbf' = 'how-to-use xbf'
        'ui\Views\ControllerPage.xbf' = 'controller xbf'
        'ui\Views\SettingsPage.xbf' = 'settings xbf'
        'ui\Views\DeveloperPage.xbf' = 'developer xbf'
        'qam\SteamInputAddonforClaw.QamHost.exe' = 'qam executable'
        'qam\Frontend\qam.js' = 'qam frontend'
        'overlay\SteamInputAddonforClaw.Overlay.exe' = 'overlay executable'
        'overlay\SteamInputAddonforClaw.Overlay.dll' = 'overlay managed payload'
        'overlay\SteamInputAddonforClaw.Overlay.pri' = 'overlay application pri'
        'overlay\App.xbf' = 'overlay app xbf'
        'overlay\OverlayWindow.xbf' = 'overlay window xbf'
        'fse\SteamInputAddonforClaw.FseHome.exe' = 'fse executable'
        'fse\SteamInputAddonforClaw.FseHome.dll' = 'fse managed payload'
        'fse\SteamInputAddonforClaw.FseHome.msix' = (Join-Path $repoRoot 'src\SteamInputAddonforClaw.FseHome\Packaging\Distribution\SteamInputAddonforClaw.FseHome.msix')
        'fse\SteamInputAddonforClaw.FseHome.cer' = (Join-Path $repoRoot 'src\SteamInputAddonforClaw.FseHome\Packaging\Distribution\SteamInputAddonforClaw.FseHome.cer')
    }

    foreach ($entry in $files.GetEnumerator()) {
        $destination = Join-Path $root $entry.Key
        $sourceExists = $false
        if ($entry.Value -is [string]) {
            try { $sourceExists = Test-Path -LiteralPath $entry.Value -PathType Leaf }
            catch [ArgumentException] { $sourceExists = $false }
        }
        if ($sourceExists) {
            Copy-Item -LiteralPath $entry.Value -Destination $destination
        }
        else {
            Set-Content -LiteralPath $destination -Value $entry.Value
        }
    }

    return $root
}

function Invoke-Verify {
    param([Parameter(Mandatory)] [string] $PublishDirectory)
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath, '-PublishDirectory', $PublishDirectory)
        $output = & powershell.exe @arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    [pscustomobject]@{ ExitCode = $exitCode; Output = ($output | Out-String) }
}

function Assert-Success {
    param([Parameter(Mandatory)] $Result, [Parameter(Mandatory)] [string] $Case)
    if ($Result.ExitCode -ne 0) { throw "Expected '$Case' to succeed, but it failed with output:`n$($Result.Output)" }
}

function Assert-MissingAssetFailure {
    param([Parameter(Mandatory)] $Result, [Parameter(Mandatory)] [string] $Case, [Parameter(Mandatory)] [string] $Asset)
    if ($Result.ExitCode -eq 0) { throw "Expected '$Case' to fail, but it succeeded." }
    if ($Result.Output -notmatch [regex]::Escape($Asset)) { throw "Expected '$Case' to report missing '$Asset', but got:`n$($Result.Output)" }
}

$fixturesToClean = @()
try {
    $validRoot = New-Fixture
    $fixturesToClean += $validRoot
    Assert-Success -Result (Invoke-Verify -PublishDirectory $validRoot) -Case 'complete application asset set'

    $root = New-Fixture
    $fixturesToClean += $root
    Set-Content -LiteralPath (Join-Path $root 'Dependencies\HidHide\HidHide_1.5.230_x64.exe') -Value 'forbidden HidHide installer'
    $result = Invoke-Verify -PublishDirectory $root
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'HidHide prerequisite installer') {
        throw 'Expected a bundled HidHide prerequisite installer to be rejected.'
    }

    $root = New-Fixture
    $fixturesToClean += $root
    Set-Content -LiteralPath (Join-Path $root 'Dependencies\UsbIpWin2\USBip-0.9.8.0-x64.exe') -Value 'forbidden usbip installer'
    $result = Invoke-Verify -PublishDirectory $root
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'usbip-win2 prerequisite installer') {
        throw 'Expected a bundled usbip-win2 prerequisite installer to be rejected.'
    }

    $root = New-Fixture
    $fixturesToClean += $root
    Remove-Item -LiteralPath (Join-Path $root 'fse\SteamInputAddonforClaw.FseHome.msix')
    Assert-MissingAssetFailure -Result (Invoke-Verify -PublishDirectory $root) -Case 'missing final FSE MSIX' -Asset 'fse\SteamInputAddonforClaw.FseHome.msix'

    $root = New-Fixture
    $fixturesToClean += $root
    Set-Content -LiteralPath (Join-Path $root 'Dependencies\ClawHUD\ClawHUDRuntime.zip') -Value 'forbidden ClawHUD Runtime payload'
    $result = Invoke-Verify -PublishDirectory $root
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'ClawHUDRuntime.zip') {
        throw 'Expected a bundled ClawHUD Runtime ZIP to be rejected.'
    }

    $root = New-Fixture
    $fixturesToClean += $root
    Set-Content -LiteralPath (Join-Path $root 'Dependencies\ClawHUD\ClawHUD.exe') -Value 'forbidden ClawHUD Runtime payload'
    $result = Invoke-Verify -PublishDirectory $root
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'ClawHUD.exe') {
        throw 'Expected a bundled ClawHUD executable to be rejected.'
    }

    $root = New-Fixture
    $fixturesToClean += $root
    Set-Content -LiteralPath (Join-Path $root 'coreclr.dll') -Value 'forbidden runtime payload'
    $result = Invoke-Verify -PublishDirectory $root
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'runtime payload') {
        throw 'Expected a root self-contained runtime payload to be rejected.'
    }

    $root = New-Fixture
    $fixturesToClean += $root
    Set-Content -LiteralPath (Join-Path $root 'qam\hostpolicy.dll') -Value 'forbidden runtime payload'
    $result = Invoke-Verify -PublishDirectory $root
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'runtime payload') {
        throw 'Expected a QAM self-contained runtime payload to be rejected.'
    }

    $root = New-Fixture
    $fixturesToClean += $root
    Set-Content -LiteralPath (Join-Path $root 'qam\Microsoft.Windows.SDK.NET.dll') -Value 'forbidden Windows SDK projection'
    $result = Invoke-Verify -PublishDirectory $root
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'Microsoft.Windows.SDK.NET.dll') {
        throw 'Expected the QAM Windows SDK projection payload to be rejected.'
    }

    $root = New-Fixture
    $fixturesToClean += $root
    Set-Content -LiteralPath (Join-Path $root 'ui\Microsoft.WindowsAppRuntime.dll') -Value 'forbidden self-contained Windows App SDK payload'
    $result = Invoke-Verify -PublishDirectory $root
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'self-contained Windows App SDK payload') {
        throw 'Expected a self-contained Windows App SDK payload to be rejected.'
    }

    $root = New-Fixture
    $fixturesToClean += $root
    Set-Content -LiteralPath (Join-Path $root 'ui\System.Private.CoreLib.dll') -Value 'forbidden runtime payload'
    $result = Invoke-Verify -PublishDirectory $root
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'runtime payload') {
        throw 'Expected a UI self-contained runtime payload to be rejected.'
    }

    $root = New-Fixture
    $fixturesToClean += $root
    Remove-Item -LiteralPath (Join-Path $root 'ui\SteamInputAddonforClaw.UI.pri')
    Assert-MissingAssetFailure -Result (Invoke-Verify -PublishDirectory $root) -Case 'dependency PRI without application PRI' -Asset 'ui\SteamInputAddonforClaw.UI.pri'

    $root = New-Fixture
    $fixturesToClean += $root
    Remove-Item -LiteralPath (Join-Path $root 'ui\MainWindow.xbf')
    Assert-MissingAssetFailure -Result (Invoke-Verify -PublishDirectory $root) -Case 'missing MainWindow.xbf' -Asset 'ui\MainWindow.xbf'

    $root = New-Fixture
    $fixturesToClean += $root
    Remove-Item -LiteralPath (Join-Path $root 'ui\Views\HowToUsePage.xbf')
    Assert-MissingAssetFailure -Result (Invoke-Verify -PublishDirectory $root) -Case 'missing child-view XBF' -Asset 'ui\Views\HowToUsePage.xbf'

    $root = New-Fixture
    $fixturesToClean += $root
    Remove-Item -LiteralPath (Join-Path $root 'fse\SteamInputAddonforClaw.FseHome.cer')
    Assert-MissingAssetFailure -Result (Invoke-Verify -PublishDirectory $root) -Case 'missing FSE public certificate' -Asset 'fse\SteamInputAddonforClaw.FseHome.cer'

    $root = New-Fixture
    $fixturesToClean += $root
    Remove-Item -LiteralPath (Join-Path $root 'SteamInputAddonforClaw.TdpHelper.exe')
    Assert-MissingAssetFailure -Result (Invoke-Verify -PublishDirectory $root) -Case 'missing TDP helper artifact' -Asset 'SteamInputAddonforClaw.TdpHelper.exe'

    Write-Host 'Publish asset verification tests passed.'
}
finally {
    foreach ($fixture in $fixturesToClean) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
