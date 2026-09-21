function Invoke-SdkTool {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [ValidateRange(1, 600)] [int]$TimeoutSeconds,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$Operation
    )

    $process = [System.Diagnostics.Process]::new()
    try {
        $argumentList = ($Arguments | ForEach-Object {
            if ($_ -match '[\s"]') {
                '"' + $_.Replace('"', '\"') + '"'
            }
            else {
                $_
            }
        }) -join ' '

        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $FilePath
        $startInfo.Arguments = $argumentList
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $process.StartInfo = $startInfo

        Write-Host "Starting ${Operation}: $FilePath $argumentList"
        if (-not $process.Start()) {
            throw "Unable to start ${Operation}: $FilePath"
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "${Operation} timed out after $TimeoutSeconds seconds: $FilePath"
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Host $stdout.TrimEnd() }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) { Write-Host $stderr.TrimEnd() }

        if ($process.ExitCode -ne 0) {
            throw "${Operation} failed with exit code $($process.ExitCode): $FilePath"
        }

        Write-Host "Completed $Operation with exit code 0."
    }
    finally {
        $process.Dispose()
    }
}
