[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$PublishDirectory,
    [string]$OutputPath,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$CertificatePath,
    [Parameter(Mandatory)] [SecureString]$CertificatePassword,
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')] [string]$FseVersion = '1.0.0.0',
    [string]$DistributionDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'invoke-sdk-tool.ps1')

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

    foreach ($root in ($roots | Where-Object { $_ } | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        $versionDirectories = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending)
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

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "FSE Home publish directory was not found: $PublishDirectory"
}
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw "The requested FSE Home signing certificate was not found: $CertificatePath"
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$distribution = if ([string]::IsNullOrWhiteSpace($DistributionDirectory)) {
    Join-Path $repositoryRoot 'src\SteamInputAddonforClaw.FseHome\Packaging\Distribution'
} else {
    [System.IO.Path]::GetFullPath($DistributionDirectory)
}
$output = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $distribution 'SteamInputAddonforClaw.FseHome.msix'
} else {
    [System.IO.Path]::GetFullPath($OutputPath)
}
$certificateOutput = Join-Path (Split-Path -Parent $output) 'SteamInputAddonforClaw.FseHome.cer'
$packageSource = Join-Path ([System.IO.Path]::GetTempPath()) ("SteamInputAddonforClaw-fse-package-" + [Guid]::NewGuid().ToString('N'))

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $CertificatePath,
    $CertificatePassword,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
try {
    if ($certificate.Subject -ne 'CN=SteamInputAddonforClaw') {
        throw "FSE Home signing certificate subject '$($certificate.Subject)' does not match the package publisher."
    }

    New-Item -ItemType Directory -Path $packageSource, (Split-Path -Parent $output) -Force | Out-Null
    Get-ChildItem -LiteralPath $PublishDirectory -File | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $packageSource -Force
    New-Item -ItemType Directory -Path (Join-Path $packageSource 'Public'), (Join-Path $packageSource 'Assets') -Force | Out-Null
    $manifest = Get-Content (Join-Path $repositoryRoot 'src\SteamInputAddonforClaw.FseHome\Packaging\AppxManifest.xml') -Raw
    $manifest = $manifest.Replace('Version="1.0.0.0"', ('Version="' + $FseVersion + '"'))
    Set-Content -LiteralPath (Join-Path $packageSource 'AppxManifest.xml') -Value $manifest -Encoding UTF8
    Copy-Item (Join-Path $repositoryRoot 'src\SteamInputAddonforClaw.FseHome\Packaging\CustomCapability.SCCD') (Join-Path $packageSource 'CustomCapability.SCCD') -Force
    Copy-Item (Join-Path $repositoryRoot 'src\SteamInputAddonforClaw.FseHome\Packaging\Public\README.txt') (Join-Path $packageSource 'Public\README.txt') -Force
    Copy-Item (Join-Path $repositoryRoot 'src\SteamInputAddonforClaw\Assets\AppIcon.png') (Join-Path $packageSource 'Assets\AppIcon.png') -Force

    $makeAppx = Find-SdkTool 'makeappx'
    $makePri = Find-SdkTool 'makepri'
    $signTool = Find-SdkTool 'signtool'
    $resourceConfig = Join-Path $packageSource 'priconfig.xml'
    Invoke-SdkTool -FilePath $makePri -Arguments @('createconfig', '/cf', $resourceConfig, '/dq', 'en-US', '/o') -TimeoutSeconds 60 -Operation 'makepri createconfig'
    Invoke-SdkTool -FilePath $makePri -Arguments @('new', '/pr', $packageSource, '/cf', $resourceConfig, '/of', (Join-Path $packageSource 'resources.pri')) -TimeoutSeconds 60 -Operation 'makepri new'
    Remove-Item -LiteralPath $resourceConfig -Force
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
    Invoke-SdkTool -FilePath $makeAppx -Arguments @('pack', '/d', $packageSource, '/p', $output, '/overwrite') -TimeoutSeconds 60 -Operation 'makeappx pack'

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword)
    try {
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        Invoke-SdkTool -FilePath $signTool -Arguments @('sign', '/fd', 'SHA256', '/f', $CertificatePath, '/p', $password, $output) -TimeoutSeconds 60 -Operation 'signtool sign'
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        $password = $null
    }

    Export-Certificate -Cert $certificate -FilePath $certificateOutput -Force | Out-Null
    Write-Host "Created fixed FSE distribution artifacts: $output and $certificateOutput"
}
finally {
    $certificate.Dispose()
    if (Test-Path -LiteralPath $packageSource) {
        Remove-Item -LiteralPath $packageSource -Recurse -Force -ErrorAction SilentlyContinue
    }
}
