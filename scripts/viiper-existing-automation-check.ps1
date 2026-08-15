<#
Pure classification for the VIIPER dependency-update receiver's idempotency
check: given the outcome of the two remote lookups (an existing automation
PR for the target branch, and whether the target branch itself already
exists), decides whether adoption may proceed. Kept separate from the
network calls themselves so it's testable without gh/git/network access.

Fails closed (throws) if either lookup itself failed -- a failed lookup must
never be treated as "does not exist".
#>

function Get-ViiperExistingAutomationClassification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [int] $PrQueryExitCode,
        [Parameter(Mandatory)] [int] $ExistingPrCount,
        [Parameter(Mandatory)] [int] $LsRemoteExitCode,
        [Parameter(Mandatory)] [int] $RemoteRefCount,
        [Parameter(Mandatory)] [string] $Branch
    )

    if ($PrQueryExitCode -ne 0) {
        throw "Failed to query existing automation PRs for branch '$Branch' (gh exit code $PrQueryExitCode). Failing closed rather than assuming none exist."
    }

    if ($ExistingPrCount -gt 0) {
        return 'PrExists'
    }

    if ($LsRemoteExitCode -ne 0) {
        throw "Failed to determine whether remote branch '$Branch' exists (git ls-remote exit code $LsRemoteExitCode). Failing closed rather than assuming it does not exist."
    }

    if ($RemoteRefCount -gt 0) {
        return 'BranchExists'
    }

    return 'Clean'
}

if ($MyInvocation.InvocationName -eq '.') {
    return
}
