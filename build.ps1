# WitchDrawer Build Script
# Usage: .\build.ps1 [-Release] [-SkipTests]

param(
    [switch]$Release,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$Configuration = if ($Release) { "Release" } else { "Debug" }
$Solution = Join-Path $PSScriptRoot "WitchDrawer.sln"
$RustDir = Join-Path $PSScriptRoot "rust\witchdrawer-core"

Write-Host "=== WitchDrawer Build ($Configuration) ===" -ForegroundColor Cyan

# Step 1: Build Rust DLL
Write-Host "`n[1/3] Building Rust core..." -ForegroundColor Yellow
Push-Location $RustDir
try {
    cargo fmt --all -- --check
    if ($LASTEXITCODE -ne 0) { throw "Rust formatting check failed" }
    cargo clippy --all-targets -- -D warnings
    if ($LASTEXITCODE -ne 0) { throw "Rust Clippy failed" }
    cargo build --release
    if ($LASTEXITCODE -ne 0) { throw "Rust build failed" }
}
finally {
    Pop-Location
}
Write-Host "  -> witchdrawer_core.dll built" -ForegroundColor Green

# Step 2: Build .NET solution
Write-Host "`n[2/3] Restoring and building .NET projects..." -ForegroundColor Yellow
dotnet restore $Solution
if ($LASTEXITCODE -ne 0) { throw ".NET restore failed" }

# Cargo already ran above; prevent the Core project from invoking it again.
dotnet build $Solution -c $Configuration --no-restore -p:SkipRustBuild=true
if ($LASTEXITCODE -ne 0) { throw ".NET build failed" }

# Step 3: Run tests
if (-not $SkipTests) {
    Write-Host "`n[3/3] Running tests..." -ForegroundColor Yellow
    
    dotnet test $Solution -c $Configuration --no-build --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw ".NET tests failed" }
    Write-Host "  -> .NET tests passed" -ForegroundColor Green
    
    Push-Location $RustDir
    try {
        cargo test --lib
        if ($LASTEXITCODE -ne 0) { throw "Rust tests failed" }
    }
    finally {
        Pop-Location
    }
    Write-Host "  -> Rust tests passed" -ForegroundColor Green
} else {
    Write-Host "`n[3/3] Skipping tests" -ForegroundColor Gray
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Cyan
Write-Host "Output: $(Join-Path $PSScriptRoot "src\WitchDrawer.App\bin\$Configuration\net10.0-windows")"
