# WitchDrawer Build Script
# Usage: .\build.ps1 [-Release] [-SkipTests] [-SkipRust]

param(
    [switch]$Release,
    [switch]$SkipTests,
    [switch]$SkipRust
)

$ErrorActionPreference = "Stop"
$Configuration = if ($Release) { "Release" } else { "Debug" }
$Solution = Join-Path $PSScriptRoot "WitchDrawer.sln"
$RustDir = Join-Path $PSScriptRoot "rust\witchdrawer-core"
$ManagedProjects = @(
    (Join-Path $PSScriptRoot "src\WitchDrawer.App\WitchDrawer.App.csproj"),
    (Join-Path $PSScriptRoot "tests\WitchDrawer.Core.Tests\WitchDrawer.Core.Tests.csproj"),
    (Join-Path $PSScriptRoot "tests\WitchDrawer.App.Tests\WitchDrawer.App.Tests.csproj")
)
$ManagedTestProjects = $ManagedProjects | Where-Object { $_ -like "*Tests.csproj" }

Write-Host "=== WitchDrawer Build ($Configuration) ===" -ForegroundColor Cyan

# Step 1: Build Rust DLL
if (-not $SkipRust) {
    Write-Host "`n[1/3] Building Rust core..." -ForegroundColor Yellow
    Push-Location $RustDir
    try {
        cargo build --release
        if ($LASTEXITCODE -ne 0) { throw "Rust build failed" }
    }
    finally {
        Pop-Location
    }
    Write-Host "  -> witchdrawer_core.dll built" -ForegroundColor Green
} else {
    Write-Host "`n[1/3] Skipping Rust bridge build and tests" -ForegroundColor Gray
}

# Step 2: Build .NET solution
Write-Host "`n[2/3] Restoring and building .NET projects..." -ForegroundColor Yellow
dotnet restore $Solution
if ($LASTEXITCODE -ne 0) { throw ".NET restore failed" }

if ($SkipRust) {
    foreach ($Project in $ManagedProjects) {
        dotnet build $Project -c $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) { throw ".NET build failed: $Project" }
    }
} else {
    # Cargo already ran above; prevent the RustBridge project from invoking it again.
    dotnet build $Solution -c $Configuration --no-restore -p:SkipRustBuild=true
    if ($LASTEXITCODE -ne 0) { throw ".NET build failed" }
}

# Step 3: Run tests
if (-not $SkipTests) {
    Write-Host "`n[3/3] Running tests..." -ForegroundColor Yellow
    
    if ($SkipRust) {
        foreach ($Project in $ManagedTestProjects) {
            dotnet test $Project -c $Configuration --no-build --verbosity minimal
            if ($LASTEXITCODE -ne 0) { throw ".NET tests failed: $Project" }
        }
    } else {
        dotnet test $Solution -c $Configuration --no-build --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw ".NET tests failed" }
    }
    Write-Host "  -> .NET tests passed" -ForegroundColor Green
    
    # Rust tests
    if (-not $SkipRust) {
        Push-Location $RustDir
        try {
            cargo test --lib
            if ($LASTEXITCODE -ne 0) { throw "Rust tests failed" }
        }
        finally {
            Pop-Location
        }
        Write-Host "  -> Rust tests passed" -ForegroundColor Green
    }
} else {
    Write-Host "`n[3/3] Skipping tests" -ForegroundColor Gray
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Cyan
Write-Host "Output: $(Join-Path $PSScriptRoot "src\WitchDrawer.App\bin\$Configuration\net10.0-windows")"
