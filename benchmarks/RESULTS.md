# Rust Core Migration Performance

Measured on 2026-07-28 after switching the production Core implementation from
C# to Rust. The baseline is commit `9cf98ec`, whose WPF application still uses
the C# SQLite/file services. The current WPF application uses the Rust-backed
services compiled into `WitchDrawer.Core`.

## Environment

- Windows 10.0.26200
- .NET 10.0.10
- Intel Core i5-11400H, 6 cores / 12 logical processors
- 16 GB physical memory
- x64 Release builds

## Production application startup and idle memory

Both applications are x64 Release builds. Each trial uses a fresh data
directory and starts with `--silent`. Engine order alternates across seven
trials. Startup is ready only after WPF has become input-idle, SQLite exists,
and the app logs `Application startup complete.` after loading the main list,
quick panel, and desktop boxes. The baseline was instrumented with only this
same final log line so both executables expose an equivalent ready point.
Memory is sampled five seconds later.

| Metric | C# baseline median | Rust current median | Rust vs C# |
|---|---:|---:|---:|
| Complete silent startup | 1823.55 ms | 1724.85 ms | 5.4% faster |
| Idle working set | 173.81 MB | 184.51 MB | 6.2% more |
| Idle private memory | 112.90 MB | 125.68 MB | 11.3% more |

The production migration improves complete cold-start readiness modestly, but
it does **not** reduce stable idle memory on this machine. The native runtime,
bundled SQLite, and update stack add about 10.7 MB working set and 12.8 MB
private memory at the five-second sample. Earlier database-only readiness
produced much larger apparent startup gains, but was rejected because the UI
and lists were not ready yet.

## Core workload comparison

### Method

`benchmarks/BenchDotnet` launches each engine in a fresh .NET child process.
The engine order alternates between trials. One warm-up trial is discarded,
then seven measured trials are aggregated using the median.

The workload creates 200 mapping items and 100 todos, then performs 100
full-list reads and 100 searches returning up to 200 items. Memory values are
process growth relative to the same child process immediately before service
initialization. Consequently, both engines include the same .NET host cost;
Rust measurements additionally include the native DLL and FFI JSON bridge.

| Metric | C# median | Rust median | Rust vs C# |
|---|---:|---:|---:|
| Core host cold start | 544.20 ms | 458.62 ms | 15.7% faster |
| Service + schema initialization | 377.69 ms | 332.94 ms | 11.8% faster |
| Init working-set growth | 10.85 MB | 11.52 MB | 6.2% more |
| Init private-memory growth | 2.07 MB | 1.91 MB | 7.5% less |
| Populate 200 items + 100 todos | 7800.25 ms | 7572.30 ms | 2.9% faster |
| Read all 200 items | 8191.23 us/op | 7443.12 us/op | 9.1% faster |
| Search, limit 200 | 8868.17 us/op | 8375.11 us/op | 5.6% faster |
| Workload working-set growth | 30.70 MB | 25.47 MB | 17.0% less |
| Workload private-memory growth | 16.98 MB | 12.63 MB | 25.6% less |

## Interpretation

The Rust Core is faster across this populated workload and uses less incremental
memory after sustained work. Immediately after service initialization its
working set is slightly larger while private-memory growth is slightly lower.
This workload-growth result and the production idle result answer different
questions: Rust retains less additional memory while
processing data, but its fixed runtime cost makes the empty idle app larger.

The "Core host cold start" measurement includes process launch, Core/schema
initialization, JSON output, and process exit. It is not a WPF first-frame or
hotkey-to-panel measurement; use the production startup table for app-level
impact.

## Reproduce

```powershell
dotnet run -c Release --project benchmarks\BenchDotnet\BenchDotnet.csproj

.\benchmarks\AppStartupComparison.ps1 `
  -BaselineExecutable <csharp-baseline-exe> `
  -CurrentExecutable .\src\WitchDrawer.App\bin\Release\net10.0-windows\WitchDrawer.App.exe `
  -Trials 7 -IdleDelayMilliseconds 5000
```

Process memory and cold-start measurements vary with Windows file cache,
antivirus activity, power mode, and background load. Compare medians on the
same machine rather than individual trials across different machines.
