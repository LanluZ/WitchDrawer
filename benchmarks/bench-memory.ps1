# Memory comparison: Rust-core build vs C#-original build (--silent, 12s settle)
# Usage: powershell -File bench-memory.ps1

$ErrorActionPreference = "Continue"
$rustExe = "D:\Users\LanluZ\Documents\WitchDrawer\src\WitchDrawer.App\bin\Release\net10.0-windows\WitchDrawer.App.exe"
$csharpExe = "D:\d\tmp\wd-csharp-main\src\WitchDrawer.App\bin\Release\net10.0-windows\WitchDrawer.App.exe"

function Measure-App([string]$exePath, [string]$label) {
    $results = @()
    foreach ($trial in 1..3) {
        Get-Process WitchDrawer.App -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 800
        $p = Start-Process -FilePath $exePath -ArgumentList "--silent" -PassThru
        Start-Sleep -Seconds 12
        $p.Refresh()
        $ws = [math]::Round($p.WorkingSet64 / 1MB, 1)
        $priv = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
        $results += [PSCustomObject]@{ Trial = $trial; WS_MB = $ws; Priv_MB = $priv }
        Write-Host ("{0}  trial{1}: WS={2}MB Priv={3}MB" -f $label, $trial, $ws, $priv)
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
    $med = $results | Sort-Object WS_MB | Select-Object -Index 1
    Write-Host ("== {0} median: WS={1}MB Priv={2}MB ==" -f $label, $med.WS_MB, $med.Priv_MB)
    return $med
}

Write-Host "=== Bench start (C# first, then Rust) ==="
$c = Measure-App $csharpExe "CSharp"
$r = Measure-App $rustExe "Rust"

Write-Host ""
Write-Host "========== RESULT =========="
$wsDiff = [math]::Round(($r.WS_MB - $c.WS_MB) / $c.WS_MB * 100, 1)
$privDiff = [math]::Round(($r.Priv_MB - $c.Priv_MB) / $c.Priv_MB * 100, 1)
Write-Host ("CSharp : WS={0}MB  Priv={1}MB" -f $c.WS_MB, $c.Priv_MB)
Write-Host ("Rust   : WS={0}MB  Priv={1}MB" -f $r.WS_MB, $r.Priv_MB)
Write-Host ("WS diff: {0}%   Priv diff: {1}%" -f $wsDiff, $privDiff)
Get-Process WitchDrawer.App -ErrorAction SilentlyContinue | Stop-Process -Force
