function Get-ReleasesToPrune {
    param(
        [Parameter(Mandatory)]
        [array] $Releases,

        [int] $RetainCount = 5
    )

    $publishedReleases = @($Releases | Where-Object { -not $_.draft -and -not $_.prerelease })
    if ($publishedReleases.Count -le $RetainCount) {
        return @()
    }

    return $publishedReleases |
        Sort-Object { [DateTimeOffset]$_.created_at } -Descending |
        Select-Object -Skip $RetainCount
}
