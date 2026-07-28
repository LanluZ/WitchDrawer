# WitchDrawer Build Script
# Usage: .\build.ps1 [-Release] [-SkipTests] [-SkipRust]

param(
    [switch]$Release,
    [switch]$SkipTests,
    [switch]$SkipRust
)

$ErrorActionPreference = "Stop"
$Configuration = if ($Release) { "Release" } else { "Debug" }
$RustDir = Join-Path $PSScriptRoot "rust\witchdrawer-core"
$DllSource = Join-Path $RustDir "target\release\witchdrawer_core.dll"
$DllDestDir = "src\WitchDrawer.App\bin\$Configuration\net10.0-windows"

Write-Host "=== WitchDrawer Build ($Configuration) ===" -ForegroundColor Cyan

# Step 1: Build Rust DLL
if (-not $SkipRust) {
    Write-Host "`n[1/3] Building Rust core..." -ForegroundColor Yellow
    Push-Location $RustDir
    cargo build --release
    if ($LASTEXITCODE -ne 0) { throw "Rust build failed" }
    Pop-Location
    Write-Host "  -> witchdrawer_core.dll built" -ForegroundColor Green
} else {
    Write-Host "`n[1/3] Skipping Rust build" -ForegroundColor Gray
}

# Step 2: Build .NET solution
Write-Host "`n[2/3] Building .NET solution..." -ForegroundColor Yellow
dotnet build WitchDrawer.sln -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw ".NET build failed" }

# Copy DLL to output
if (-not $SkipRust) {
    Copy-Item $DllSource $DllDestDir -Force
    Write-Host "  -> DLL copied to $DllDestDir" -ForegroundColor Green
}

# Step 3: Run tests
if (-not $SkipTests) {
    Write-Host "`n[3/3] Running tests..." -ForegroundColor Yellow
    
    # .NET tests
    dotnet test WitchDrawer.sln -c $Configuration --no-build --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw ".NET tests failed" }
    Write-Host "  -> .NET tests passed" -ForegroundColor Green
    
    # Rust tests
    if (-not $SkipRust) {
        Push-Location $RustDir
        cargo test --lib
        if ($LASTEXITCODE -ne 0) { throw "Rust tests failed" }
        Pop-Location
        Write-Host "  -> Rust tests passed" -ForegroundColor Green
    }
} else {
    Write-Host "`n[3/3] Skipping tests" -ForegroundColor Gray
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Cyan
Write-Host "Output: src\WitchDrawer.App\bin\$Configuration\net10.0-windows\"
