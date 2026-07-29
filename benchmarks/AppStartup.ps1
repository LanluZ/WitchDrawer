param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [int]$Trials = 7,
    [int]$IdleDelayMilliseconds = 1500
)

$ErrorActionPreference = "Stop"
$resolvedExecutable = (Resolve-Path $Executable).Path
$trialResults = @()

for ($trial = 0; $trial -lt $Trials; $trial++) {
    $dataRoot = Join-Path $env:TEMP ("WitchDrawer.StartupBenchmark." + [guid]::NewGuid().ToString("N"))
    [IO.Directory]::CreateDirectory($dataRoot) | Out-Null

    $startInfo = [Diagnostics.ProcessStartInfo]::new($resolvedExecutable)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add("--silent")
    $startInfo.Environment["WITCHDRAWER_DATA_DIR"] = $dataRoot

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $inputIdle = $process.WaitForInputIdle(15000)
        $databasePath = Join-Path $dataRoot "witchdrawer.db"
        $logPath = Join-Path (Join-Path $dataRoot "logs") ((Get-Date).ToString("yyyy-MM-dd") + ".log")
        $startupComplete = $false
        while (-not $process.HasExited -and -not $startupComplete) {
            if ($stopwatch.ElapsedMilliseconds -ge 15000) {
                break
            }
            if (Test-Path -LiteralPath $logPath) {
                try {
                    $startupComplete = (Get-Content -LiteralPath $logPath -Raw) -match "Application startup complete\."
                }
                catch [IO.IOException] {
                    # The logger may briefly hold the file while appending the marker.
                }
            }
            Start-Sleep -Milliseconds 25
        }

        if (-not $inputIdle -or $process.HasExited -or -not (Test-Path -LiteralPath $databasePath) -or -not $startupComplete) {
            throw "App did not reach the startup-ready condition."
        }

        $readyMilliseconds = $stopwatch.Elapsed.TotalMilliseconds
        Start-Sleep -Milliseconds $IdleDelayMilliseconds
        $process.Refresh()
        $trialResults += [pscustomobject]@{
            ReadyMilliseconds = $readyMilliseconds
            WorkingSetMB = $process.WorkingSet64 / 1MB
            PrivateMemoryMB = $process.PrivateMemorySize64 / 1MB
        }
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()

        $fullDataRoot = [IO.Path]::GetFullPath($dataRoot)
        $fullTempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)
        $isUnderTemp = $fullDataRoot.StartsWith(
            $fullTempRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)
        $isBenchmarkDirectory = [IO.Path]::GetFileName($fullDataRoot).StartsWith(
            "WitchDrawer.StartupBenchmark.",
            [StringComparison]::Ordinal)
        if ($isUnderTemp -and $isBenchmarkDirectory) {
            [IO.Directory]::Delete($fullDataRoot, $true)
        }
    }
}

function Get-Median([double[]]$Values) {
    $ordered = $Values | Sort-Object
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if ($ordered.Count % 2 -eq 0) {
        return ($ordered[$middle - 1] + $ordered[$middle]) / 2
    }
    return $ordered[$middle]
}

$summary = [pscustomobject]@{
    Executable = $resolvedExecutable
    Trials = $Trials
    ReadyMillisecondsMedian = Get-Median @($trialResults.ReadyMilliseconds)
    WorkingSetMBMedian = Get-Median @($trialResults.WorkingSetMB)
    PrivateMemoryMBMedian = Get-Median @($trialResults.PrivateMemoryMB)
}

$summary | ConvertTo-Json -Depth 3
