$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $repoRoot 'scripts\verify-viiper.ps1'
$realViiperDir = Join-Path $repoRoot 'src\SteamInputAddonforClaw\Dependencies\Viiper'

function New-Fixture {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("viiper-verify-test-" + [System.Guid]::NewGuid())
    $viiperDir = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper'
    $docsDir = Join-Path $root 'docs'
    New-Item -ItemType Directory -Force -Path $viiperDir | Out-Null
    New-Item -ItemType Directory -Force -Path $docsDir | Out-Null

    Copy-Item -LiteralPath (Join-Path $realViiperDir 'libVIIPER.dll') -Destination $viiperDir
    Copy-Item -LiteralPath (Join-Path $realViiperDir 'libVIIPER.h') -Destination $viiperDir
    Copy-Item -LiteralPath (Join-Path $realViiperDir 'viiper.lock.json') -Destination $viiperDir
    Copy-Item -LiteralPath (Join-Path $realViiperDir 'PROVENANCE.md') -Destination $viiperDir
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\VIIPER_INTEGRATION.md') -Destination $docsDir
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\VIIPER_MIGRATION_TODO.md') -Destination $docsDir

    return $root
}

function Invoke-Verify {
    param([Parameter(Mandatory)] [string] $Root)

    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath -RepoRoot $Root 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
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

$fixturesToClean = @()

try {
    # 1. Valid current dependency succeeds.
    $validRoot = New-Fixture
    $fixturesToClean += $validRoot
    Assert-Success -Result (Invoke-Verify -Root $validRoot) -Case 'valid current dependency'

    # 2. Wrong DLL hash fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $lockPath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\viiper.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $lock.dll.sha256 = '0' * 64
    $lock | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lockPath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'wrong DLL hash'

    # 3. Wrong header hash fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $lockPath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\viiper.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $lock.header.sha256 = '0' * 64
    $lock | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lockPath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'wrong header hash'

    # 4. Malformed commit fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $lockPath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\viiper.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $lock.commit = 'not-a-commit'
    $lock | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lockPath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'malformed commit'

    # 5. Wrong repository fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $lockPath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\viiper.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $lock.repository = 'someone-else/VIIPER'
    $lock | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lockPath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'wrong repository'

    # 6. Wrong build entrypoint fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $lockPath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\viiper.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $lock.build_entrypoint = 'make release'
    $lock | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lockPath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'wrong build entrypoint'

    # 7. Wrong platform/architecture fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $lockPath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\viiper.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $lock.platform = 'linux'
    $lock.architecture = 'arm64'
    $lock | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lockPath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'wrong platform/architecture'

    # 8. Wrong DLL/header filename fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $lockPath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\viiper.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $lock.dll.file = 'libVIIPER2.dll'
    $lock.header.file = 'libVIIPER2.h'
    $lock | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lockPath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'wrong DLL/header filename'

    # 9. PROVENANCE commit mismatch fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $provenancePath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\PROVENANCE.md'
    (Get-Content -LiteralPath $provenancePath -Raw) -replace 'Commit:\s*[0-9a-fA-F]{40}', ('Commit:     ' + ('1' * 40)) |
        Set-Content -LiteralPath $provenancePath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'PROVENANCE commit mismatch'

    # 10. PROVENANCE DLL hash mismatch fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $provenancePath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\PROVENANCE.md'
    (Get-Content -LiteralPath $provenancePath -Raw) -replace 'DLL SHA-256:\s*[0-9a-fA-F]{64}', ('DLL SHA-256:              ' + ('0' * 64)) |
        Set-Content -LiteralPath $provenancePath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'PROVENANCE DLL hash mismatch'

    # 11. PROVENANCE header hash mismatch fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $provenancePath = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper\PROVENANCE.md'
    (Get-Content -LiteralPath $provenancePath -Raw) -replace 'Generated header SHA-256:\s*[0-9a-fA-F]{64}', ('Generated header SHA-256: ' + ('0' * 64)) |
        Set-Content -LiteralPath $provenancePath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'PROVENANCE header hash mismatch'

    # 12. Authoritative Addon documentation pin mismatch fails.
    $root = New-Fixture
    $fixturesToClean += $root
    $integrationDocPath = Join-Path $root 'docs\VIIPER_INTEGRATION.md'
    (Get-Content -LiteralPath $integrationDocPath -Raw) -replace 'Embedded VIIPER revision \| `[0-9a-fA-F]{40}`', ('Embedded VIIPER revision | `' + ('2' * 40) + '`') |
        Set-Content -LiteralPath $integrationDocPath
    Assert-Failure -Result (Invoke-Verify -Root $root) -Case 'authoritative documentation pin mismatch'

    Write-Host 'VIIPER verification script tests passed.'
}
finally {
    foreach ($fixture in $fixturesToClean) {
        Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
