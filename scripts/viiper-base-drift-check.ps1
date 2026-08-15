<#
Pure classification for the VIIPER dependency-update receiver's stale-base
guard: given the Addon `main` commit the current adoption run actually began
from and the latest `origin/main` commit resolved immediately before pushing
the automation branch / opening the Draft PR, decides whether it is still
safe to proceed.

This exists because the automation's mechanical adoption (artifact fetch,
`adopt-viiper.ps1`, build, tests) all run against a `main` snapshot captured
at checkout time. If a different PR merges into `main` while that work is in
flight (e.g. the previous VIIPER dependency PR merging while the next one's
automation run is already executing), pushing the automation branch or
opening a PR from the stale snapshot would produce a mechanical commit whose
base no longer matches `main` -- guaranteed conflicts across the same
dependency identity files, resolved incorrectly if a human just picks
ours/theirs instead of re-running the tooling from the current base.

Fails closed (throws) whenever the comparison itself cannot be trusted --
an unresolved latest-main SHA, or a missing/malformed base or latest SHA --
never silently treated as "base is still current".
#>

function Get-ViiperBaseDriftClassification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $AdoptionBaseSha,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $LatestMainSha,
        [Parameter(Mandatory)] [int] $ResolveLatestMainExitCode
    )

    if ($ResolveLatestMainExitCode -ne 0) {
        throw "Failed to resolve the latest origin/main commit (exit code $ResolveLatestMainExitCode). Failing closed rather than assuming the adoption base is still current."
    }

    if ([string]::IsNullOrWhiteSpace($AdoptionBaseSha) -or $AdoptionBaseSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Adoption base SHA is missing or not exactly 40 hex characters: '$AdoptionBaseSha'. Failing closed rather than guessing the base this adoption run started from."
    }

    if ([string]::IsNullOrWhiteSpace($LatestMainSha) -or $LatestMainSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Latest origin/main SHA is missing or not exactly 40 hex characters: '$LatestMainSha'. Failing closed rather than guessing whether main advanced."
    }

    if ($AdoptionBaseSha.ToLowerInvariant() -ne $LatestMainSha.ToLowerInvariant()) {
        return 'StaleBase'
    }

    return 'BaseCurrent'
}

function Get-ViiperPostPushBaseDriftAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $AdoptionBaseSha,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $LatestMainSha,
        [Parameter(Mandatory)] [int] $ResolveLatestMainExitCode,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $LocalHeadSha,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $RemoteBranchHeadSha
    )

    $classification = Get-ViiperBaseDriftClassification `
        -AdoptionBaseSha $AdoptionBaseSha `
        -LatestMainSha $LatestMainSha `
        -ResolveLatestMainExitCode $ResolveLatestMainExitCode

    foreach ($name in 'LocalHeadSha', 'RemoteBranchHeadSha') {
        $value = Get-Variable -Name $name -ValueOnly
        if ([string]::IsNullOrWhiteSpace($value) -or $value -notmatch '^[0-9a-fA-F]{40}$') {
            throw "$name is missing or not exactly 40 hex characters: '$value'. Failing closed rather than guessing branch ownership."
        }
    }

    if ($classification -eq 'BaseCurrent') {
        return 'Proceed'
    }

    if ($RemoteBranchHeadSha.ToLowerInvariant() -eq $LocalHeadSha.ToLowerInvariant()) {
        return 'DeleteBranchAndFailClosed'
    }

    return 'FailClosedPreserveBranch'
}

if ($MyInvocation.InvocationName -eq '.') {
    return
}
