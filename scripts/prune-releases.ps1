function Get-ReleasesToPrune {
    param(
        [Parameter(Mandatory)]
        [array] $Releases,

        [int] $StableRetainCount = 5,

        [int] $PreReleaseRetainCount = 1
    )

    $publishedReleases = @($Releases | Where-Object { -not $_.draft })
    $stableReleases = @($publishedReleases | Where-Object { -not $_.prerelease })
    $preReleases = @($publishedReleases | Where-Object { $_.prerelease })

    $stableToPrune = @()
    if ($stableReleases.Count -gt $StableRetainCount) {
        $stableToPrune = @(
            $stableReleases |
                Sort-Object { [DateTimeOffset]$_.published_at } -Descending |
                Select-Object -Skip $StableRetainCount
        )
    }

    $preToPrune = @()
    if ($preReleases.Count -gt $PreReleaseRetainCount) {
        $preToPrune = @(
            $preReleases |
                Sort-Object { [DateTimeOffset]$_.published_at } -Descending |
                Select-Object -Skip $PreReleaseRetainCount
        )
    }

    return @($stableToPrune) + @($preToPrune)
}
