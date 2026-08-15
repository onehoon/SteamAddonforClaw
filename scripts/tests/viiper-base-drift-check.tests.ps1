$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $repoRoot 'scripts\viiper-base-drift-check.ps1'

. $scriptPath

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

function Assert-ThrowsWithMessage {
    param([Parameter(Mandatory)] [scriptblock] $ScriptBlock, [Parameter(Mandatory)] [string] $Case, [string] $MessageContains)
    $threw = $false
    try {
        & $ScriptBlock
    }
    catch {
        $threw = $true
        if ($MessageContains -and $_.Exception.Message -notmatch [regex]::Escape($MessageContains)) {
            throw "Expected '$Case' to throw a message containing '$MessageContains', actual: $($_.Exception.Message)"
        }
    }
    if (-not $threw) {
        throw "Expected '$Case' to throw, but it did not."
    }
}

$baseA = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$baseB = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'

try {
    # 1. Base unchanged -> BaseCurrent, safe to proceed.
    $result = Get-ViiperBaseDriftClassification -AdoptionBaseSha $baseA -LatestMainSha $baseA -ResolveLatestMainExitCode 0
    Assert-Equal -Expected 'BaseCurrent' -Actual $result -Message 'Unchanged base not classified as BaseCurrent.'

    # 1b. Case-insensitive comparison -- same commit, different casing, still BaseCurrent.
    $result = Get-ViiperBaseDriftClassification -AdoptionBaseSha $baseA -LatestMainSha $baseA.ToUpperInvariant() -ResolveLatestMainExitCode 0
    Assert-Equal -Expected 'BaseCurrent' -Actual $result -Message 'Case-differing but identical base not classified as BaseCurrent.'

    # 2. Main advanced during adoption -> StaleBase, fail closed at the workflow layer.
    $result = Get-ViiperBaseDriftClassification -AdoptionBaseSha $baseA -LatestMainSha $baseB -ResolveLatestMainExitCode 0
    Assert-Equal -Expected 'StaleBase' -Actual $result -Message 'Advanced main not classified as StaleBase.'

    # 3. Unable to resolve latest origin/main (nonzero exit, e.g. network/auth) -> fail closed.
    Assert-ThrowsWithMessage -Case 'unresolved latest main' -MessageContains 'Failing closed rather than assuming the adoption base is still current' -ScriptBlock {
        Get-ViiperBaseDriftClassification -AdoptionBaseSha $baseA -LatestMainSha '' -ResolveLatestMainExitCode 1
    }

    # 4. Invalid/empty adoption base SHA -> fail closed.
    Assert-ThrowsWithMessage -Case 'empty adoption base SHA' -MessageContains 'Adoption base SHA is missing or not exactly 40 hex characters' -ScriptBlock {
        Get-ViiperBaseDriftClassification -AdoptionBaseSha '' -LatestMainSha $baseA -ResolveLatestMainExitCode 0
    }
    Assert-ThrowsWithMessage -Case 'malformed adoption base SHA' -MessageContains 'Adoption base SHA is missing or not exactly 40 hex characters' -ScriptBlock {
        Get-ViiperBaseDriftClassification -AdoptionBaseSha 'not-a-sha' -LatestMainSha $baseA -ResolveLatestMainExitCode 0
    }

    # 5. Invalid/empty latest main SHA -> fail closed (even though the resolve step itself reported success).
    Assert-ThrowsWithMessage -Case 'empty latest main SHA' -MessageContains 'Latest origin/main SHA is missing or not exactly 40 hex characters' -ScriptBlock {
        Get-ViiperBaseDriftClassification -AdoptionBaseSha $baseA -LatestMainSha '' -ResolveLatestMainExitCode 0
    }
    Assert-ThrowsWithMessage -Case 'malformed latest main SHA' -MessageContains 'Latest origin/main SHA is missing or not exactly 40 hex characters' -ScriptBlock {
        Get-ViiperBaseDriftClassification -AdoptionBaseSha $baseA -LatestMainSha 'short' -ResolveLatestMainExitCode 0
    }

    # 6. Real race regression: an automation run begins from base A, completes mechanical adoption,
    #    build, and tests entirely against that stale snapshot, and only then -- immediately before the
    #    branch push / Draft PR -- discovers that main has already advanced to B (e.g. a prior VIIPER
    #    dependency PR merged while this run was in flight, exactly the #176/#177 scenario). The guard
    #    must block the push/PR at that final checkpoint rather than let the stale-base commit through.
    $adoptionBase = $baseA
    # ... mechanical adoption, build, tests all completed successfully against $adoptionBase ...
    $latestMainAfterAdoptionWork = $baseB
    Assert-ThrowsWithMessage -Case 'main advanced while adoption work was in flight' -MessageContains 'Aborting stale-base dependency PR creation' -ScriptBlock {
        $classification = Get-ViiperBaseDriftClassification -AdoptionBaseSha $adoptionBase -LatestMainSha $latestMainAfterAdoptionWork -ResolveLatestMainExitCode 0
        if ($classification -eq 'StaleBase') {
            throw "Addon main changed during VIIPER dependency adoption: base=$adoptionBase current=$latestMainAfterAdoptionWork. Aborting stale-base dependency PR creation."
        }
    }

    Write-Host 'viiper-base-drift-check.ps1 tests passed.'
}
catch {
    throw
}
