[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublishDirectory,

    [string]$RepoRoot,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($RepoRoot)) {
    $scriptDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path))
    $RepoRoot = Split-Path -Parent $scriptDirectory
}

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish/output directory was not found: $PublishDirectory"
}

$sourceBinary = Join-Path $PublishDirectory 'CenterMHelperSource\CenterMHelper.exe'
if (-not (Test-Path -LiteralPath $sourceBinary -PathType Leaf)) {
    throw "CenterMHelper publish output not found at: $sourceBinary"
}

$smokeProject = Join-Path $RepoRoot 'src\SteamInputAddonforClaw.CenterMHelperSmoke\SteamInputAddonforClaw.CenterMHelperSmoke.csproj'
$smokeExe = Join-Path $RepoRoot "src\SteamInputAddonforClaw.CenterMHelperSmoke\bin\$Configuration\net10.0-windows10.0.26100.0\win-x64\SteamInputAddonforClaw.CenterMHelperSmoke.exe"

# Built as a plain console executable and run directly here (not via `dotnet test`/VSTest) --
# VSTest's own process-tree management was found to interfere with the CreateProcess(SUSPENDED) +
# Job sequence this smoke check exercises.
& dotnet build $smokeProject -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Failed to build $smokeProject" }

if (-not (Test-Path -LiteralPath $smokeExe -PathType Leaf)) {
    throw "Smoke tool executable not found after build: $smokeExe"
}

& $smokeExe $PublishDirectory | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "CenterM helper smoke check failed (exit code $LASTEXITCODE) -- the staged/renamed helper artifact did not start and stay alive, or did not stop cleanly through retained ownership."
}

Write-Host 'CenterM helper smoke check passed: staged artifact started, stayed alive, and stopped cleanly.'
