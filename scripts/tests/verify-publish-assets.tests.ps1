$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $repoRoot 'scripts\verify-publish-assets.ps1'
$realPublish = Join-Path $repoRoot 'artifacts\publish-assets-shutdown'

function Invoke-Verify {
    param([Parameter(Mandatory)] [string] $PublishDirectory)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath -PublishDirectory $PublishDirectory 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output   = ($output | Out-String)
    }
}

function Assert-Success {
    param([Parameter(Mandatory)] $Result, [Parameter(Mandatory)] [string] $Case)
    if ($Result.ExitCode -ne 0) {
        throw "Expected '$Case' to succeed, but it failed with output:`n$($Result.Output)"
    }
}

function Assert-Failure {
    param([Parameter(Mandatory)] $Result, [Parameter(Mandatory)] [string] $Case)
    if ($Result.ExitCode -eq 0) {
        throw "Expected '$Case' to fail, but it succeeded with output:`n$($Result.Output)"
    }
}

if (-not (Test-Path -LiteralPath $realPublish -PathType Container)) {
    throw "The checked-in publish fixture is missing: $realPublish"
}

$fixturesToClean = @()
try {
    $validRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("publish-assets-verify-test-" + [System.Guid]::NewGuid())
    Copy-Item -LiteralPath $realPublish -Destination $validRoot -Recurse
    $fixturesToClean += $validRoot
    Assert-Success -Result (Invoke-Verify -PublishDirectory $validRoot) -Case 'complete application asset set'

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("publish-assets-verify-test-" + [System.Guid]::NewGuid())
    Copy-Item -LiteralPath $realPublish -Destination $root -Recurse
    $fixturesToClean += $root
    Remove-Item -LiteralPath (Join-Path $root 'ui\SteamInputAddonforClaw.UI.pri')
    Set-Content -LiteralPath (Join-Path $root 'ui\dependency.pri') -Value 'dependency'
    Assert-Failure -Result (Invoke-Verify -PublishDirectory $root) -Case 'dependency PRI without application PRI'

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("publish-assets-verify-test-" + [System.Guid]::NewGuid())
    Copy-Item -LiteralPath $realPublish -Destination $root -Recurse
    $fixturesToClean += $root
    Remove-Item -LiteralPath (Join-Path $root 'ui\MainWindow.xbf')
    Assert-Failure -Result (Invoke-Verify -PublishDirectory $root) -Case 'missing MainWindow.xbf'

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("publish-assets-verify-test-" + [System.Guid]::NewGuid())
    Copy-Item -LiteralPath $realPublish -Destination $root -Recurse
    $fixturesToClean += $root
    Remove-Item -LiteralPath (Join-Path $root 'ui\Views\HowToUsePage.xbf')
    Assert-Failure -Result (Invoke-Verify -PublishDirectory $root) -Case 'missing child-view XBF'

    Write-Host 'Publish asset verification tests passed.'
}
finally {
    foreach ($fixture in $fixturesToClean) {
        Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
