[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$PublishDirectory,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$OutputPath,
    [string]$CertificatePath,
    [SecureString]$CertificatePassword,
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'fse\Package\AppxManifest.xml') -PathType Leaf)) {
    throw 'FSE Home publish assets are missing. Run publish-layout.ps1 first.'
}

function Find-SdkTool([string]$name) {
    $command = Get-Command "$name.exe" -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $roots = @()
    if (-not [string]::IsNullOrWhiteSpace($env:WindowsSdkDir)) {
        $roots += Join-Path $env:WindowsSdkDir 'bin'
    }
    $roots += @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
    )

    foreach ($root in ($roots | Where-Object { $_ } | Select-Object -Unique) ) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }

        $versionDirectories = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending)
        foreach ($architecture in @('x64', 'x86', 'arm64')) {
            $directCandidate = Join-Path $root "$architecture\$name.exe"
            if (Test-Path -LiteralPath $directCandidate -PathType Leaf) {
                return (Get-Item -LiteralPath $directCandidate).FullName
            }

            foreach ($versionDirectory in $versionDirectories) {
                $candidate = Join-Path $versionDirectory.FullName "$architecture\$name.exe"
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return (Get-Item -LiteralPath $candidate).FullName
                }
            }
        }
    }

    throw "$name.exe was not found in the Windows SDK."
}

if ([string]::IsNullOrWhiteSpace($CertificatePath) -or $null -eq $CertificatePassword) {
    throw 'A signing certificate is required to create a registerable FSE Home package. Pass -CertificatePath and -CertificatePassword; no private signing key is stored in the repository.'
}
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw 'The requested FSE Home signing certificate was not found.'
}

$makeAppx = Find-SdkTool 'makeappx'
$signTool = Find-SdkTool 'signtool'
$packageSource = Join-Path $PublishDirectory 'fse\Package'
$output = [System.IO.Path]::GetFullPath($OutputPath)
$manifestPath = Join-Path $packageSource 'AppxManifest.xml'
$manifestXml = [System.Xml.Linq.XDocument]::Load($manifestPath)
$identityElement = $manifestXml.Root.Elements() | Where-Object { $_.Name.LocalName -eq 'Identity' } | Select-Object -First 1
if ($null -eq $identityElement) { throw 'FSE Home package manifest does not contain an Identity element.' }
$identityName = $identityElement.Attribute('Name')
if ($null -eq $identityName -or $identityName.Value -ne 'SteamInputAddonforClaw.FseHome') { throw 'FSE Home package identity is not stable.' }
$manifestVersion = $identityElement.Attribute('Version').Value
if ($ExpectedVersion -and $manifestVersion -ne $ExpectedVersion) {
    throw "FSE Home package version '$manifestVersion' does not match expected release version '$ExpectedVersion'."
}
$manifestPublisher = $identityElement.Attribute('Publisher').Value
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $CertificatePath,
    $CertificatePassword,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
if ($certificate.Subject -ne $manifestPublisher) {
    throw "FSE Home manifest Publisher '$manifestPublisher' does not match signing certificate subject '$($certificate.Subject)'."
}
$applications = @($manifestXml.Descendants() | Where-Object {
    $id = $_.Attribute('Id')
    $_.Name.LocalName -eq 'Application' -and $null -ne $id -and $id.Value -eq 'App'
})
if ($applications.Count -eq 0) {
    throw 'FSE Home package manifest is missing Application Id App.'
}
if (-not (Get-Content -LiteralPath $manifestPath -Raw | Select-String -SimpleMatch 'windows.gamingApp')) {
    throw 'FSE Home package manifest is missing the windows.gamingApp extension.'
}
if (-not (Get-Content -LiteralPath $manifestPath -Raw | Select-String -SimpleMatch 'Microsoft.appCategory.gamingHome_8wekyb3d8bbwe')) {
    throw 'FSE Home package manifest is missing the Gaming Home custom capability.'
}
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }

& $makeAppx pack /d $packageSource /p $output /overwrite
if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE." }

$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword)
try {
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    & $signTool sign /fd SHA256 /f $CertificatePath /p $password $output
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    $password = $null
}
Write-Host "Signed FSE Home package: $output"
