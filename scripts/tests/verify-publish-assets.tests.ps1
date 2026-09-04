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
        (Join-Path $root 'ui\Views'),
        (Join-Path $root 'qam\Frontend'),
        (Join-Path $root 'overlay')
    )
    New-Item -ItemType Directory -Force -Path $directories | Out-Null

    $files = @{
        'SteamInputAddonforClaw.exe' = 'runtime'
        'SteamInputAddonforClaw.TdpHelper.exe' = 'tdp helper'
        'Dependencies\HidHide\HidHide_1.5.230_x64.exe' = (Join-Path $dependencyRoot 'HidHide\HidHide_1.5.230_x64.exe')
        'Dependencies\UsbIpWin2\USBip-0.9.7.7-x64.exe' = (Join-Path $dependencyRoot 'UsbIpWin2\USBip-0.9.7.7-x64.exe')
        'Dependencies\Viiper\libVIIPER.dll' = (Join-Path $dependencyRoot 'Viiper\libVIIPER.dll')
        'Dependencies\Viiper\PROVENANCE.md' = (Join-Path $dependencyRoot 'Viiper\PROVENANCE.md')
        'Dependencies\Viiper\libVIIPER.h' = (Join-Path $dependencyRoot 'Viiper\libVIIPER.h')
        'Dependencies\Viiper\LICENSE.txt' = (Join-Path $dependencyRoot 'Viiper\LICENSE.txt')
        'ui\SteamInputAddonforClaw.UI.exe' = 'ui executable'
        'ui\SteamInputAddonforClaw.UI.dll' = 'managed payload'
        'ui\SteamInputAddonforClaw.UI.pri' = 'application pri'
        'ui\App.xbf' = 'app xbf'
        'ui\MainWindow.xbf' = 'main window xbf'
        'ui\Views\HowToUsePage.xbf' = 'how-to-use xbf'
        'ui\Views\ControllerPage.xbf' = 'controller xbf'
        'ui\Views\SettingsPage.xbf' = 'settings xbf'
        'ui\Views\DeveloperPage.xbf' = 'developer xbf'
        'ui\Microsoft.WindowsAppRuntime.1.8.pri' = 'dependency pri'
        'ui\Microsoft.UI.Xaml.winmd' = 'winmd payload'
        'qam\SteamInputAddonforClaw.QamHost.exe' = 'qam executable'
        'qam\Frontend\qam.js' = 'qam frontend'
        'overlay\SteamInputAddonforClaw.Overlay.exe' = 'overlay executable'
        'overlay\SteamInputAddonforClaw.Overlay.dll' = 'overlay managed payload'
        'overlay\SteamInputAddonforClaw.Overlay.pri' = 'overlay application pri'
        'overlay\App.xbf' = 'overlay app xbf'
        'overlay\OverlayWindow.xbf' = 'overlay window xbf'
        'overlay\Microsoft.UI.Xaml.winmd' = 'overlay winmd payload'
    }

    foreach ($entry in $files.GetEnumerator()) {
        $destination = Join-Path $root $entry.Key
        if ($entry.Value -is [string] -and (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
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
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath -PublishDirectory $PublishDirectory 2>&1
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
    Remove-Item -LiteralPath (Join-Path $root 'SteamInputAddonforClaw.TdpHelper.exe')
    Assert-MissingAssetFailure -Result (Invoke-Verify -PublishDirectory $root) -Case 'missing TDP helper artifact' -Asset 'SteamInputAddonforClaw.TdpHelper.exe'

    Write-Host 'Publish asset verification tests passed.'
}
finally {
    foreach ($fixture in $fixturesToClean) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
