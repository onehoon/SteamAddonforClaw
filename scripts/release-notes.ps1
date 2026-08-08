[CmdletBinding()]
param(
    [string]$PreviousTag,

    [Parameter(Mandatory)]
    [string]$CurrentSha,

    [Parameter(Mandatory)]
    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$range = if ($PreviousTag) { "$PreviousTag..$CurrentSha" } else { $CurrentSha }
$subjects = git log --format=%s $range
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read git history for $range."
}

$changes = [System.Collections.Generic.List[string]]::new()
foreach ($subject in $subjects) {
    $change = $subject
    if ($subject -match '#(?<number>\d+)') {
        $prTitle = gh pr view $Matches.number --json title --jq .title 2>$null
        if ($LASTEXITCODE -eq 0 -and $prTitle) {
            $change = $prTitle
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($change) -and -not $changes.Contains($change)) {
        $changes.Add($change)
    }
}

$content = [System.Collections.Generic.List[string]]::new()
$content.Add('## Changes')
$content.Add('')
if ($changes.Count -eq 0) {
    $content.Add('- No user-visible changes were recorded.')
}
else {
    foreach ($change in $changes) {
        $content.Add("- $change")
    }
}

$parentDirectory = Split-Path -Parent $OutputFile
if ($parentDirectory) {
    New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
}

Set-Content -LiteralPath $OutputFile -Value $content -Encoding utf8
