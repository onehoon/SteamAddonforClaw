[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$PublishDirectory,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$OutputPath,
    [string]$CertificatePath,
    [SecureString]$CertificatePassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'fse\Package\AppxManifest.xml') -PathType Leaf)) {
    throw 'FSE Home publish assets are missing. Run publish-layout.ps1 first.'
}

function Find-SdkTool([string]$name) {
    $command = Get-Command "$name.exe" -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
    ) | Where-Object { $_ -and (Test-Path $_) }
    $tool = Get-ChildItem -Path $roots -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($tool) { return $tool.FullName }
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
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }

& $makeAppx pack /d $packageSource /p $output /overwrite
if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE." }

$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword)
try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
& $signTool sign /fd SHA256 /f $CertificatePath /p $password $output
if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }
Write-Host "Signed FSE Home package: $output"
