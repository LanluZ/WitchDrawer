param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineExecutable,
    [Parameter(Mandatory = $true)]
    [string]$CurrentExecutable,
    [int]$Trials = 7,
    [int]$IdleDelayMilliseconds = 1500
)

$ErrorActionPreference = "Stop"
$startupBenchmark = Join-Path $PSScriptRoot "AppStartup.ps1"
$baselinePath = (Resolve-Path $BaselineExecutable).Path
$currentPath = (Resolve-Path $CurrentExecutable).Path
$results = @()

for ($trial = 0; $trial -lt $Trials; $trial++) {
    $baseline = [pscustomobject]@{ Engine = "C#"; Executable = $baselinePath }
    $current = [pscustomobject]@{ Engine = "Rust"; Executable = $currentPath }
    $order = if ($trial % 2 -eq 0) { @($baseline, $current) } else { @($current, $baseline) }

    foreach ($entry in $order) {
        $measurement = & $startupBenchmark `
            -Executable $entry.Executable `
            -Trials 1 `
            -IdleDelayMilliseconds $IdleDelayMilliseconds | ConvertFrom-Json

        $results += [pscustomobject]@{
            Trial = $trial + 1
            Engine = $entry.Engine
            ReadyMilliseconds = [double]$measurement.ReadyMillisecondsMedian
            WorkingSetMB = [double]$measurement.WorkingSetMBMedian
            PrivateMemoryMB = [double]$measurement.PrivateMemoryMBMedian
        }
    }
}

function Get-Median([double[]]$Values) {
    $ordered = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if ($ordered.Count % 2 -eq 0) {
        return ($ordered[$middle - 1] + $ordered[$middle]) / 2
    }
    return $ordered[$middle]
}

$summaries = @{}
foreach ($engine in @("C#", "Rust")) {
    $rows = @($results | Where-Object Engine -eq $engine)
    $summaries[$engine] = [pscustomobject]@{
        Engine = $engine
        Trials = $rows.Count
        ReadyMillisecondsMedian = Get-Median @($rows.ReadyMilliseconds)
        WorkingSetMBMedian = Get-Median @($rows.WorkingSetMB)
        PrivateMemoryMBMedian = Get-Median @($rows.PrivateMemoryMB)
    }
}

$csharp = $summaries["C#"]
$rust = $summaries["Rust"]
$comparison = [pscustomobject]@{
    ReadyTimeReductionPercent = 100 * ($csharp.ReadyMillisecondsMedian - $rust.ReadyMillisecondsMedian) / $csharp.ReadyMillisecondsMedian
    WorkingSetReductionPercent = 100 * ($csharp.WorkingSetMBMedian - $rust.WorkingSetMBMedian) / $csharp.WorkingSetMBMedian
    PrivateMemoryReductionPercent = 100 * ($csharp.PrivateMemoryMBMedian - $rust.PrivateMemoryMBMedian) / $csharp.PrivateMemoryMBMedian
}

[pscustomobject]@{
    BaselineExecutable = $baselinePath
    CurrentExecutable = $currentPath
    TrialsPerEngine = $Trials
    Results = $results
    Summary = @($csharp, $rust)
    Comparison = $comparison
} | ConvertTo-Json -Depth 5
