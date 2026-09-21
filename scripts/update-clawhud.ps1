[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Tag,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$LockPath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$StagingDirectory,

    [string]$DispatchRuntimeVersion,
    [string]$DispatchTag,
    [string]$DispatchSourceCommit,
    [string]$DispatchAsset,
    [string]$DispatchSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repository = 'onehoon/ClawHUD'
$expectedAsset = 'ClawHUDRuntime.zip'
$expectedAssets = @($expectedAsset, 'ClawHUDRuntime.zip.sha256', 'runtime-manifest.json')

function Write-OutputValue {
    param([string]$Name, [string]$Value)

    if ($env:GITHUB_OUTPUT) {
        "$Name=$Value" >> $env:GITHUB_OUTPUT
    }
}

function Read-ManifestString {
    param([object]$Manifest, [string]$Name)

    $property = $Manifest.PSObject.Properties[$Name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Runtime manifest field '$Name' is missing or empty."
    }
    return [string]$property.Value
}

function Assert-DispatchValue {
    param([string]$Name, [string]$Provided, [string]$Actual)

    if (-not [string]::IsNullOrWhiteSpace($Provided) -and $Provided -cne $Actual) {
        throw "repository_dispatch client_payload.$Name does not match the independently verified release."
    }
}

if ($Tag -notmatch '^steamaddon-runtime-v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "Runtime tag must be exactly steamaddon-runtime-vMAJOR.MINOR.PATCH. Got: '$Tag'."
}
$targetVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3])"

$releaseJson = gh api "repos/$repository/releases/tags/$Tag"
if ($LASTEXITCODE -ne 0) {
    throw "Could not query the exact ClawHUD release for tag '$Tag'."
}
$release = $releaseJson | ConvertFrom-Json
if ($release.tag_name -cne $Tag) { throw "Release tag mismatch: expected '$Tag', got '$($release.tag_name)'." }
if ($release.draft -or -not $release.prerelease) { throw "ClawHUD release '$Tag' must be a non-draft prerelease." }

$assets = @($release.assets)
$assetNames = @($assets | ForEach-Object { [string]$_.name } | Sort-Object)
$expectedSorted = @($expectedAssets | Sort-Object)
if (($assetNames -join '|') -cne ($expectedSorted -join '|')) {
    throw "ClawHUD release '$Tag' must contain exactly: $($expectedSorted -join ', '). Got: $($assetNames -join ', ')."
}
foreach ($asset in $assets) {
    if ($asset.state -and $asset.state -cne 'uploaded') {
        throw "ClawHUD release asset '$($asset.name)' is not fully uploaded (state=$($asset.state))."
    }
}

if (Test-Path -LiteralPath $StagingDirectory) {
    Remove-Item -LiteralPath $StagingDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingDirectory -Force | Out-Null
foreach ($assetName in $expectedAssets) {
    gh release download $Tag --repo $repository --pattern $assetName --dir $StagingDirectory --clobber
    if ($LASTEXITCODE -ne 0) { throw "Could not download exact release asset '$assetName' for '$Tag'." }
}

$zipPath = Join-Path $StagingDirectory $expectedAsset
$sidecarPath = Join-Path $StagingDirectory 'ClawHUDRuntime.zip.sha256'
$manifestPath = Join-Path $StagingDirectory 'runtime-manifest.json'
foreach ($path in @($zipPath, $sidecarPath, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Downloaded release asset is missing: $path" }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([int]$manifest.schema_version -ne 1) { throw 'ClawHUD Runtime manifest schema_version must be 1.' }
$manifestVersion = Read-ManifestString $manifest 'runtime_version'
$manifestTag = Read-ManifestString $manifest 'tag'
$manifestSourceCommit = Read-ManifestString $manifest 'source_commit'
$manifestAsset = Read-ManifestString $manifest 'asset'
$manifestSha256 = Read-ManifestString $manifest 'sha256'
if ($manifestVersion -cne $targetVersion) { throw "Runtime manifest version '$manifestVersion' does not match tag version '$targetVersion'." }
if ($manifestTag -cne $Tag) { throw "Runtime manifest tag '$manifestTag' does not match requested tag '$Tag'." }
if ($manifestSourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'Runtime manifest source_commit must be exactly 40 hexadecimal characters.' }
if ($manifestAsset -cne $expectedAsset) { throw "Runtime manifest asset must be '$expectedAsset'." }
if ($manifestSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Runtime manifest sha256 must be exactly 64 hexadecimal characters.' }

$tagRefJson = gh api "repos/$repository/git/ref/tags/$Tag"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve Runtime tag '$Tag'." }
$tagRef = $tagRefJson | ConvertFrom-Json
$tagTargetSha = [string]$tagRef.object.sha
if ($tagRef.object.type -eq 'tag') {
    $annotatedJson = gh api "repos/$repository/git/tags/$tagTargetSha"
    if ($LASTEXITCODE -ne 0) { throw "Could not dereference annotated Runtime tag '$Tag'." }
    $annotated = $annotatedJson | ConvertFrom-Json
    $tagTargetSha = [string]$annotated.object.sha
}
if ($tagTargetSha -notmatch '^[0-9a-fA-F]{40}$' -or $tagTargetSha.ToLowerInvariant() -cne $manifestSourceCommit.ToLowerInvariant()) {
    throw 'Runtime tag target does not match runtime-manifest source_commit.'
}

$actualSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -cne $manifestSha256.ToLowerInvariant()) { throw 'Downloaded Runtime ZIP SHA-256 does not match runtime-manifest.json.' }
$sidecarSha256 = ((Get-Content -LiteralPath $sidecarPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
if ($sidecarSha256 -cne $actualSha256) { throw 'Downloaded Runtime ZIP SHA-256 does not match the sidecar.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries)
    if ($entries.Count -eq 0) { throw 'Runtime ZIP is empty.' }
    foreach ($entry in $entries) {
        $normalized = $entry.FullName.Replace('\', '/')
        $segments = $normalized.Split('/', [StringSplitOptions]::RemoveEmptyEntries)
        $unsafeSegments = @($segments | Where-Object { $_ -in @('.', '..') })
        if ($segments.Count -eq 1 -and $segments[0] -ceq 'clawhud' -and $normalized.EndsWith('/')) { continue }
        if ($normalized.StartsWith('/') -or $normalized.Contains(':') -or
            $segments.Count -lt 2 -or $segments[0] -cne 'clawhud' -or $unsafeSegments.Count -gt 0) {
            throw "Runtime ZIP contains an unsafe or non-clawhud entry: $($entry.FullName)"
        }
    }
    $embedded = $archive.GetEntry('clawhud/runtime-manifest.json')
    if ($null -eq $embedded) { throw 'Runtime ZIP is missing clawhud/runtime-manifest.json.' }
    $reader = New-Object IO.StreamReader($embedded.Open())
    try { $embeddedManifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ([int]$embeddedManifest.schema_version -ne 1) { throw 'Embedded Runtime manifest schema_version must be 1.' }
    if ((Read-ManifestString $embeddedManifest 'runtime_version') -cne $manifestVersion -or
        (Read-ManifestString $embeddedManifest 'tag') -cne $manifestTag -or
        (Read-ManifestString $embeddedManifest 'source_commit') -cne $manifestSourceCommit -or
        (Read-ManifestString $embeddedManifest 'asset') -cne $manifestAsset) {
        throw 'Embedded Runtime manifest identity does not match the external manifest.'
    }
    if ($null -eq $embeddedManifest.PSObject.Properties['sha256'] -or $null -ne $embeddedManifest.sha256) {
        throw 'Embedded Runtime manifest sha256 must be null for schema v1.'
    }
}
finally {
    $archive.Dispose()
}

Assert-DispatchValue 'runtime_version' $DispatchRuntimeVersion $manifestVersion
Assert-DispatchValue 'tag' $DispatchTag $manifestTag
Assert-DispatchValue 'source_commit' $DispatchSourceCommit $manifestSourceCommit.ToLowerInvariant()
Assert-DispatchValue 'asset' $DispatchAsset $manifestAsset
Assert-DispatchValue 'sha256' $DispatchSha256 $manifestSha256.ToLowerInvariant()

$lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
if ([int]$lock.schema_version -ne 1) { throw 'Current ClawHUD lock schema_version must be 1.' }
foreach ($name in @('runtime_version', 'tag', 'asset', 'source_commit', 'sha256')) {
    if ($null -eq $lock.PSObject.Properties[$name]) { throw "Current ClawHUD lock is missing '$name'." }
}
$currentVersion = [version][string]$lock.runtime_version
$targetVersionObject = [version]$manifestVersion
$identityMatches = ([string]$lock.runtime_version -ceq $manifestVersion -and
    [string]$lock.tag -ceq $manifestTag -and
    [string]$lock.asset -ceq $manifestAsset -and
    [string]$lock.source_commit -ieq $manifestSourceCommit -and
    [string]$lock.sha256 -ieq $manifestSha256)

if ($targetVersionObject -eq $currentVersion) {
    if (-not $identityMatches) { throw 'Equal Runtime version has different identity; refusing to overwrite an immutable pin.' }
    $eligibility = 'NoOp'
}
elseif ($targetVersionObject -lt $currentVersion) {
    $eligibility = 'Downgrade'
}
else {
    $eligibility = 'Eligible'
}

Write-OutputValue 'eligibility' $eligibility
Write-OutputValue 'runtime_version' $manifestVersion
Write-OutputValue 'tag' $manifestTag
Write-OutputValue 'source_commit' $manifestSourceCommit.ToLowerInvariant()
Write-OutputValue 'asset' $manifestAsset
Write-OutputValue 'sha256' $manifestSha256.ToLowerInvariant()
Write-OutputValue 'current_version' ([string]$lock.runtime_version)
Write-Host "Verified ClawHUD Runtime $manifestTag ($eligibility)."
