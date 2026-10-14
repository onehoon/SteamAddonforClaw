[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$UiPublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$uiDirectory = [System.IO.Path]::GetFullPath($UiPublishDirectory)
$applicationPri = Join-Path $uiDirectory 'SteamInputAddonforClaw.UI.pri'
if (-not (Test-Path -LiteralPath $applicationPri -PathType Leaf)) {
    throw "Published Main UI application PRI was not found: $applicationPri"
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$stringsDirectory = Join-Path $repositoryRoot 'src\SteamInputAddonforClaw.UI\Strings'

function Read-SourceResources {
    param([Parameter(Mandatory)][string]$Path)

    $document = [System.Xml.XmlDocument]::new()
    try { $document.Load($Path) }
    catch { throw "Source RESW is invalid XML '$Path': $($_.Exception.Message)" }

    $resources = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    foreach ($data in $document.SelectNodes("/*[local-name()='root']/*[local-name()='data']")) {
        $key = $data.GetAttribute('name')
        $valueNode = $data.SelectSingleNode("./*[local-name()='value']")
        $value = if ($null -ne $valueNode) { $valueNode.InnerText } else { $null }
        if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($value)) {
            throw "Source RESW '$Path' contains an empty key or value."
        }
        if ($resources.ContainsKey($key)) {
            throw "Source RESW '$Path' contains duplicate key '$key'."
        }
        $resources.Add($key, $value)
    }
    return ,$resources
}

$englishResources = Read-SourceResources -Path (Join-Path $stringsDirectory 'en-US\Resources.resw')
$koreanResources = Read-SourceResources -Path (Join-Path $stringsDirectory 'ko-KR\Resources.resw')
$englishKeys = @($englishResources.Keys | Sort-Object -CaseSensitive)
$koreanKeys = @($koreanResources.Keys | Sort-Object -CaseSensitive)
if (@(Compare-Object -ReferenceObject $englishKeys -DifferenceObject $koreanKeys -CaseSensitive).Count -ne 0) {
    throw 'The en-US and ko-KR source RESW files must contain the same distinct key set.'
}

$requiredPilotKeys = @(
    'MainNav_Device.Content',
    'HowToUse_Loading.Text',
    'Settings_AppTheme_Description'
)
foreach ($key in $requiredPilotKeys) {
    if (-not $englishResources.ContainsKey($key)) {
        throw "Required PR1 localization key '$key' is missing from the source RESW files."
    }
}

function Get-MakePriPath {
    $candidates = [System.Collections.Generic.List[string]]::new()
    $pathCommand = Get-Command 'makepri.exe' -ErrorAction SilentlyContinue
    if ($null -ne $pathCommand -and (Test-Path -LiteralPath $pathCommand.Source -PathType Leaf)) {
        $candidates.Add($pathCommand.Source)
    }

    $sdkRoots = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:WindowsSdkDir)) {
        $sdkRoots.Add($env:WindowsSdkDir)
    }
    foreach ($registryPath in @(
        'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots'
    )) {
        $sdkRoot = (Get-ItemProperty -LiteralPath $registryPath -Name KitsRoot10 -ErrorAction SilentlyContinue).KitsRoot10
        if (-not [string]::IsNullOrWhiteSpace($sdkRoot)) {
            $sdkRoots.Add($sdkRoot)
        }
    }

    foreach ($sdkRoot in $sdkRoots | Select-Object -Unique) {
        $binDirectory = Join-Path $sdkRoot 'bin'
        if (-not (Test-Path -LiteralPath $binDirectory -PathType Container)) { continue }
        foreach ($versionDirectory in Get-ChildItem -LiteralPath $binDirectory -Directory -ErrorAction SilentlyContinue) {
            if ($versionDirectory.Name -notmatch '^\d+\.\d+\.\d+\.\d+$') { continue }
            $candidate = Join-Path $versionDirectory.FullName 'x64\makepri.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $candidates.Add($candidate)
            }
        }
    }

    $uniqueCandidates = @($candidates | Select-Object -Unique)
    if ($uniqueCandidates.Count -eq 0) {
        throw 'Windows SDK makepri.exe was not found. Install the Windows SDK resource tools or make makepri.exe available on PATH.'
    }

    $selected = $uniqueCandidates |
        Sort-Object -Property {
            $match = [regex]::Match($_, '\\bin\\(?<version>\d+\.\d+\.\d+\.\d+)\\x64\\makepri\.exe$', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if ($match.Success) { [version]$match.Groups['version'].Value } else { [version]'0.0' }
        } -Descending |
        Select-Object -First 1
    return $selected
}

$makePriPath = Get-MakePriPath
$dumpPath = Join-Path ([System.IO.Path]::GetTempPath()) ("SteamAddon-UiLocalization-{0}.xml" -f [guid]::NewGuid().ToString('N'))
try {
    $makePriOutput = & $makePriPath dump /if $applicationPri /of $dumpPath /dt Detailed /o 2>&1
    $makePriExitCode = $LASTEXITCODE
    if ($makePriExitCode -ne 0) {
        throw "Windows SDK makepri.exe dump failed with exit code $makePriExitCode for '$applicationPri'."
    }
    if (-not (Test-Path -LiteralPath $dumpPath -PathType Leaf)) {
        throw "Windows SDK makepri.exe did not produce a PRI dump for '$applicationPri'."
    }

    $dump = [System.Xml.XmlDocument]::new()
    try { $dump.Load($dumpPath) }
    catch { throw "Windows SDK makepri.exe produced invalid XML for '$applicationPri': $($_.Exception.Message)" }

    $primaryMaps = @($dump.SelectNodes("/*[local-name()='PriInfo']/*[local-name()='ResourceMap'][@primary='true']"))
    if ($primaryMaps.Count -ne 1) {
        throw "Expected exactly one primary resource map in '$applicationPri'; found $($primaryMaps.Count)."
    }
    $resourceMap = $primaryMaps[0]
    if ($resourceMap.GetAttribute('name') -cne 'SteamInputAddonforClaw.UI') {
        throw "Expected the published Main UI primary resource map 'SteamInputAddonforClaw.UI'; found '$($resourceMap.GetAttribute('name'))'."
    }

    $defaultLanguageQualifiers = @($dump.SelectNodes("/*[local-name()='PriInfo']/*[local-name()='QualifierInfo']/*[local-name()='Qualifiers']/*[local-name()='Qualifier'][@name='Language' and number(@scoreAsDefault) > 0]"))
    if ($defaultLanguageQualifiers.Count -ne 1 -or $defaultLanguageQualifiers[0].GetAttribute('value') -ine 'en-US') {
        throw "The published UI PRI must configure en-US as its only default language candidate; found $($defaultLanguageQualifiers.Count) default language candidates."
    }

    $qualifierLanguages = @{}
    foreach ($qualifierSet in $dump.SelectNodes("/*[local-name()='PriInfo']/*[local-name()='QualifierInfo']/*[local-name()='QualifierSets']/*[local-name()='QualifierSet']")) {
        $languageQualifier = $qualifierSet.SelectSingleNode("./*[local-name()='Qualifier'][@name='Language']")
        if ($null -ne $languageQualifier) {
            $qualifierLanguages[$qualifierSet.GetAttribute('index')] = $languageQualifier.GetAttribute('value').ToUpperInvariant()
        }
    }

    $namedResources = @($resourceMap.SelectNodes(".//*[local-name()='NamedResource']"))

    foreach ($key in $englishKeys) {
        if ($key -match '^(?<uid>.+)\.(?<property>Text|Content|Header|Description)$') {
            $uriSuffix = "/Resources/$($Matches.uid)/$($Matches.property)"
        }
        else {
            $uriSuffix = "/Resources/$key"
        }
        $matches = @($namedResources | Where-Object { $_.GetAttribute('uri').EndsWith($uriSuffix, [System.StringComparison]::OrdinalIgnoreCase) })
        if ($matches.Count -ne 1) {
            throw "Expected one published PRI resource ending '$uriSuffix'; found $($matches.Count)."
        }

        $candidateValues = foreach ($candidate in $matches[0].SelectNodes("./*[local-name()='Candidate']")) {
            $qualifierSet = $candidate.SelectSingleNode("./*[local-name()='QualifierSet']")
            $language = if ($null -ne $qualifierSet) { $qualifierLanguages[$qualifierSet.GetAttribute('index')] } else { $null }
            $value = $candidate.SelectSingleNode("./*[local-name()='Value']")
            $candidateValue = if ($null -ne $value) { $value.InnerText } else { $null }
            [pscustomobject]@{
                Language = $language
                Value = $candidateValue
            }
        }

        foreach ($language in @('EN-US', 'KO-KR')) {
            $languageCandidates = @($candidateValues | Where-Object { $_.Language -eq $language })
            if ($languageCandidates.Count -ne 1 -or [string]::IsNullOrWhiteSpace($languageCandidates[0].Value)) {
                throw "Published PRI resource '$uriSuffix' must contain exactly one non-empty $language candidate."
            }
        }
        if (@($candidateValues | Where-Object { $_.Language -notin @('EN-US', 'KO-KR') }).Count -gt 0) {
            throw "Published PRI resource '$uriSuffix' contains an unsupported language candidate."
        }
    }

    Write-Host "Published Main UI PRI verified: $($resourceMap.GetAttribute('name')); en-US default and $($englishKeys.Count) resources have non-empty en-US/ko-KR candidates."
}
finally {
    if (Test-Path -LiteralPath $dumpPath -PathType Leaf) {
        Remove-Item -LiteralPath $dumpPath -Force
    }
}
