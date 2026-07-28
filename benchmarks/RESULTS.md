# Core Performance Comparison

Measured on 2026-07-28 after the RustBridge safety and ABI fixes.

## Product impact

The production `WitchDrawer.App` still references only `WitchDrawer.Core` and
`WitchDrawer.Native`. No production App/Core/Native source file changed in the
Rust experiment, so the attributable improvement to the shipped application's
cold start, idle memory, and runtime performance is currently **0%**.

The numbers below compare the original C# Core with the experimental
RustBridge. They describe the potential of a future migration, not current WPF
application performance.

## Environment

- Windows 10.0.26200
- .NET 10.0.10
- Intel Core i5-11400H, 6 cores / 12 logical processors
- 16 GB physical memory
- x64 Release builds

## Method

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
| Core host cold start | 400.10 ms | 370.75 ms | 7.3% faster |
| Service + schema initialization | 246.61 ms | 251.64 ms | 2.0% slower |
| Init working-set growth | 10.73 MB | 11.46 MB | 6.7% more |
| Init private-memory growth | 1.97 MB | 2.23 MB | 13.5% more |
| Populate 200 items + 100 todos | 3479.48 ms | 3115.71 ms | 10.5% faster |
| Read all 200 items | 7940.45 us/op | 7845.63 us/op | 1.2% faster |
| Search, limit 200 | 9110.13 us/op | 7943.84 us/op | 12.8% faster |
| Workload working-set growth | 30.89 MB | 25.62 MB | 17.1% less |
| Workload private-memory growth | 17.35 MB | 13.24 MB | 23.7% less |

## Interpretation

RustBridge is measurably better for write-heavy work, search, and memory after
the populated workload. Plain full-list reads are effectively tied. It does
not improve service initialization and uses slightly more memory immediately
after initialization.

The "Core host cold start" measurement includes process launch, Core/schema
initialization, JSON output, and process exit. It is not a WPF first-frame or
hotkey-to-panel measurement. Before production adoption, the App must actually
use RustBridge and a separate WPF launch/first-window benchmark must be added.

## Reproduce

```powershell
dotnet run -c Release --project benchmarks\BenchDotnet\BenchDotnet.csproj
```

Process memory and cold-start measurements vary with Windows file cache,
antivirus activity, power mode, and background load. Compare medians on the
same machine rather than individual trials across different machines.
