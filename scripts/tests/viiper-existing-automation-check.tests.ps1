$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $repoRoot 'scripts\viiper-existing-automation-check.ps1'

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

$branch = 'automation/viiper-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'

try {
    # 1. Existing PR -> PrExists, no branch lookup needed to reach this classification.
    $result = Get-ViiperExistingAutomationClassification -PrQueryExitCode 0 -ExistingPrCount 1 -LsRemoteExitCode 0 -RemoteRefCount 0 -Branch $branch
    Assert-Equal -Expected 'PrExists' -Actual $result -Message 'Existing PR not classified as PrExists.'

    # 2. gh PR query failure -> fail closed, regardless of what the (never-reached) branch lookup would say.
    Assert-ThrowsWithMessage -Case 'gh PR query failure' -MessageContains 'Failing closed rather than assuming none exist' -ScriptBlock {
        Get-ViiperExistingAutomationClassification -PrQueryExitCode 1 -ExistingPrCount 0 -LsRemoteExitCode 0 -RemoteRefCount 0 -Branch $branch
    }

    # 3. Remote branch exists (no PR) -> BranchExists, fail closed at the workflow layer.
    $result = Get-ViiperExistingAutomationClassification -PrQueryExitCode 0 -ExistingPrCount 0 -LsRemoteExitCode 0 -RemoteRefCount 1 -Branch $branch
    Assert-Equal -Expected 'BranchExists' -Actual $result -Message 'Existing remote branch not classified as BranchExists.'

    # 4. Remote branch absent (modeled as git ls-remote --heads exiting 0 with zero matching refs --
    #    NOT exit code 2; --exit-code is deliberately not used, per the real-world bug this fixes).
    $result = Get-ViiperExistingAutomationClassification -PrQueryExitCode 0 -ExistingPrCount 0 -LsRemoteExitCode 0 -RemoteRefCount 0 -Branch $branch
    Assert-Equal -Expected 'Clean' -Actual $result -Message 'Absent remote branch (exit 0, zero refs) not classified as Clean.'

    # 5. git remote lookup failure (nonzero exit, e.g. network/auth) -> fail closed.
    Assert-ThrowsWithMessage -Case 'git remote lookup failure' -MessageContains 'Failing closed rather than assuming it does not exist' -ScriptBlock {
        Get-ViiperExistingAutomationClassification -PrQueryExitCode 0 -ExistingPrCount 0 -LsRemoteExitCode 128 -RemoteRefCount 0 -Branch $branch
    }

    Write-Host 'viiper-existing-automation-check.ps1 tests passed.'
}
catch {
    throw
}
