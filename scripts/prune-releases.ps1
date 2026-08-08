function Get-ReleaseToPrune {
    param(
        [Parameter(Mandatory)]
        [array] $Releases
    )

    $publishedReleases = @($Releases | Where-Object { -not $_.draft -and -not $_.prerelease })
    if ($publishedReleases.Count -lt 2) {
        return $null
    }

    return $publishedReleases |
        Sort-Object { [DateTimeOffset]$_.created_at } |
        Select-Object -First 1
}
