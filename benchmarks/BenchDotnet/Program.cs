using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;
using WitchDrawer.RustBridge;

const int TrialCount = 7;
const int MappingItemCount = 200;
const int TodoCount = 100;
const int QueryIterations = 100;

var engine = GetOption(args, "--engine");
var scenario = GetOption(args, "--scenario");
if (engine is not null && scenario is not null)
{
    var result = engine switch
    {
        "csharp" => await RunCSharpAsync(scenario),
        "rust" => RunRust(scenario),
        _ => throw new ArgumentException($"Unknown engine: {engine}")
    };
    Console.WriteLine(JsonSerializer.Serialize(result));
    return;
}

Console.WriteLine("WitchDrawer Core comparison benchmark");
Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"CPU: {Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")}");
Console.WriteLine($"Trials: {TrialCount}; items: {MappingItemCount}; todos: {TodoCount}; queries: {QueryIterations}");
Console.WriteLine();

// Warm both code paths before measured alternating trials. Each trial still
// gets a fresh process and temporary database.
await RunIsolatedAsync("csharp", "init");
await RunIsolatedAsync("rust", "init");
await RunIsolatedAsync("csharp", "workload");
await RunIsolatedAsync("rust", "workload");

var results = new Dictionary<string, List<BenchmarkResult>>
{
    ["csharp-init"] = [],
    ["rust-init"] = [],
    ["csharp-workload"] = [],
    ["rust-workload"] = []
};

for (var trial = 0; trial < TrialCount; trial++)
{
    var engines = trial % 2 == 0 ? new[] { "csharp", "rust" } : new[] { "rust", "csharp" };
    foreach (var currentEngine in engines)
    {
        results[$"{currentEngine}-init"].Add(await RunIsolatedAsync(currentEngine, "init"));
        results[$"{currentEngine}-workload"].Add(await RunIsolatedAsync(currentEngine, "workload"));
    }
}

var csharpInit = Aggregate(results["csharp-init"]);
var rustInit = Aggregate(results["rust-init"]);
var csharpWorkload = Aggregate(results["csharp-workload"]);
var rustWorkload = Aggregate(results["rust-workload"]);

Console.WriteLine($"{"Metric",-34} {"C# median",14} {"Rust median",14} {"Rust vs C#",14}");
Console.WriteLine(new string('-', 80));
PrintMetric("Core host cold start", csharpInit.ProcessWallMs, rustInit.ProcessWallMs, "ms");
PrintMetric("Service + schema initialization", csharpInit.ServiceInitMs, rustInit.ServiceInitMs, "ms");
PrintMetric("Init working-set growth", csharpInit.WorkingSetDeltaMb, rustInit.WorkingSetDeltaMb, "MB");
PrintMetric("Init private-memory growth", csharpInit.PrivateMemoryDeltaMb, rustInit.PrivateMemoryDeltaMb, "MB");
PrintMetric("Populate 200 items + 100 todos", csharpWorkload.PopulateMs, rustWorkload.PopulateMs, "ms");
PrintMetric("Read all 200 items", csharpWorkload.ReadAllUsPerOperation, rustWorkload.ReadAllUsPerOperation, "us/op");
PrintMetric("Search (limit 200)", csharpWorkload.SearchUsPerOperation, rustWorkload.SearchUsPerOperation, "us/op");
PrintMetric("Workload working-set growth", csharpWorkload.WorkingSetDeltaMb, rustWorkload.WorkingSetDeltaMb, "MB");
PrintMetric("Workload private-memory growth", csharpWorkload.PrivateMemoryDeltaMb, rustWorkload.PrivateMemoryDeltaMb, "MB");

static async Task<BenchmarkResult> RunCSharpAsync(string scenario)
{
    using var temp = new TemporaryDirectory();
    var baseline = CaptureMemory();
    var paths = new AppPaths(System.IO.Path.Combine(temp.Path, "data"));
    var repository = new DrawerRepository(paths.DatabasePath);
    var drawer = new DrawerService(paths, repository);
    var todos = new TodoService(repository);
    var stopwatch = Stopwatch.StartNew();
    await drawer.InitializeAsync();
    _ = await drawer.GetBoxesAsync();
    stopwatch.Stop();
    var afterInit = CaptureMemory();

    if (scenario == "init")
    {
        return CreateResult(stopwatch.Elapsed.TotalMilliseconds, baseline, afterInit);
    }

    var mappingBox = (await drawer.GetBoxesAsync()).Single(box => box.Type == BoxType.Mapping);
    var todoBox = await drawer.CreateBoxAsync("Benchmark Todos", BoxType.Todo);
    var sourcePath = System.IO.Path.Combine(temp.Path, "shared-source.txt");
    await File.WriteAllTextAsync(sourcePath, "benchmark");

    stopwatch.Restart();
    for (var index = 0; index < MappingItemCount; index++)
    {
        await drawer.ImportPathAsync(mappingBox.Id, sourcePath);
    }
    for (var index = 0; index < TodoCount; index++)
    {
        await todos.AddTodoAsync(todoBox.Id, $"Todo {index}");
    }
    stopwatch.Stop();
    var populateMs = stopwatch.Elapsed.TotalMilliseconds;

    _ = await drawer.GetAllItemsAsync();
    _ = await drawer.SearchItemsAsync("shared", 200);

    stopwatch.Restart();
    for (var index = 0; index < QueryIterations; index++)
    {
        _ = await drawer.GetAllItemsAsync();
    }
    stopwatch.Stop();
    var readUs = stopwatch.Elapsed.TotalMicroseconds / QueryIterations;

    stopwatch.Restart();
    for (var index = 0; index < QueryIterations; index++)
    {
        _ = await drawer.SearchItemsAsync("shared", 200);
    }
    stopwatch.Stop();
    var searchUs = stopwatch.Elapsed.TotalMicroseconds / QueryIterations;

    ForceCollection();
    var afterWorkload = CaptureMemory();
    return CreateResult(
        serviceInitMs: 0,
        baseline,
        afterWorkload,
        populateMs,
        readUs,
        searchUs);
}

static BenchmarkResult RunRust(string scenario)
{
    using var temp = new TemporaryDirectory();
    var baseline = CaptureMemory();
    var stopwatch = Stopwatch.StartNew();
    using var drawer = new RustDrawerService(System.IO.Path.Combine(temp.Path, "data"));
    _ = drawer.GetBoxes();
    stopwatch.Stop();
    var afterInit = CaptureMemory();

    if (scenario == "init")
    {
        return CreateResult(stopwatch.Elapsed.TotalMilliseconds, baseline, afterInit);
    }

    var todos = new RustTodoService(drawer);
    var mappingBox = drawer.GetBoxes().Single(box => box.Type == BoxType.Mapping);
    var todoBox = drawer.CreateBox("Benchmark Todos", BoxType.Todo);
    var sourcePath = System.IO.Path.Combine(temp.Path, "shared-source.txt");
    File.WriteAllText(sourcePath, "benchmark");

    stopwatch.Restart();
    for (var index = 0; index < MappingItemCount; index++)
    {
        drawer.ImportPath(mappingBox.Id, sourcePath);
    }
    for (var index = 0; index < TodoCount; index++)
    {
        todos.AddTodo(todoBox.Id, $"Todo {index}");
    }
    stopwatch.Stop();
    var populateMs = stopwatch.Elapsed.TotalMilliseconds;

    _ = drawer.GetAllItems();
    _ = drawer.SearchItems("shared", 200);

    stopwatch.Restart();
    for (var index = 0; index < QueryIterations; index++)
    {
        _ = drawer.GetAllItems();
    }
    stopwatch.Stop();
    var readUs = stopwatch.Elapsed.TotalMicroseconds / QueryIterations;

    stopwatch.Restart();
    for (var index = 0; index < QueryIterations; index++)
    {
        _ = drawer.SearchItems("shared", 200);
    }
    stopwatch.Stop();
    var searchUs = stopwatch.Elapsed.TotalMicroseconds / QueryIterations;

    ForceCollection();
    var afterWorkload = CaptureMemory();
    return CreateResult(
        serviceInitMs: 0,
        baseline,
        afterWorkload,
        populateMs,
        readUs,
        searchUs);
}

static async Task<BenchmarkResult> RunIsolatedAsync(string engine, string scenario)
{
    var assemblyPath = Assembly.GetExecutingAssembly().Location;
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add(assemblyPath);
    startInfo.ArgumentList.Add("--engine");
    startInfo.ArgumentList.Add(engine);
    startInfo.ArgumentList.Add("--scenario");
    startInfo.ArgumentList.Add(scenario);
    startInfo.Environment["DOTNET_NOLOGO"] = "1";

    var stopwatch = Stopwatch.StartNew();
    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start benchmark child process.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    stopwatch.Stop();
    var output = await outputTask;
    var error = await errorTask;
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"Benchmark child failed ({engine}/{scenario}): {error}");
    }

    var result = JsonSerializer.Deserialize<BenchmarkResult>(output.Trim())
        ?? throw new InvalidOperationException($"Invalid benchmark output: {output}");
    result.ProcessWallMs = stopwatch.Elapsed.TotalMilliseconds;
    return result;
}

static BenchmarkResult CreateResult(
    double serviceInitMs,
    MemorySnapshot baseline,
    MemorySnapshot after,
    double populateMs = 0,
    double readUs = 0,
    double searchUs = 0)
{
    return new BenchmarkResult
    {
        ServiceInitMs = serviceInitMs,
        WorkingSetDeltaMb = after.WorkingSetMb - baseline.WorkingSetMb,
        PrivateMemoryDeltaMb = after.PrivateMemoryMb - baseline.PrivateMemoryMb,
        PopulateMs = populateMs,
        ReadAllUsPerOperation = readUs,
        SearchUsPerOperation = searchUs
    };
}

static BenchmarkResult Aggregate(IReadOnlyList<BenchmarkResult> results)
{
    return new BenchmarkResult
    {
        ProcessWallMs = Median(results.Select(result => result.ProcessWallMs)),
        ServiceInitMs = Median(results.Select(result => result.ServiceInitMs)),
        WorkingSetDeltaMb = Median(results.Select(result => result.WorkingSetDeltaMb)),
        PrivateMemoryDeltaMb = Median(results.Select(result => result.PrivateMemoryDeltaMb)),
        PopulateMs = Median(results.Select(result => result.PopulateMs)),
        ReadAllUsPerOperation = Median(results.Select(result => result.ReadAllUsPerOperation)),
        SearchUsPerOperation = Median(results.Select(result => result.SearchUsPerOperation))
    };
}

static void PrintMetric(string name, double csharp, double rust, string unit)
{
    var deltaPercent = csharp == 0 ? 0 : (rust - csharp) / csharp * 100;
    Console.WriteLine(
        $"{name,-34} {csharp,9:F2} {unit,-4} {rust,9:F2} {unit,-4} {deltaPercent,12:+0.0;-0.0;0.0}%");
}

static double Median(IEnumerable<double> values)
{
    var ordered = values.Order().ToArray();
    var middle = ordered.Length / 2;
    return ordered.Length % 2 == 0
        ? (ordered[middle - 1] + ordered[middle]) / 2
        : ordered[middle];
}

static MemorySnapshot CaptureMemory()
{
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    return new MemorySnapshot(
        process.WorkingSet64 / 1024d / 1024d,
        process.PrivateMemorySize64 / 1024d / 1024d);
}

static void ForceCollection()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

static string? GetOption(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

internal sealed class BenchmarkResult
{
    public double ProcessWallMs { get; set; }
    public double ServiceInitMs { get; set; }
    public double WorkingSetDeltaMb { get; set; }
    public double PrivateMemoryDeltaMb { get; set; }
    public double PopulateMs { get; set; }
    public double ReadAllUsPerOperation { get; set; }
    public double SearchUsPerOperation { get; set; }
}

internal readonly record struct MemorySnapshot(double WorkingSetMb, double PrivateMemoryMb);

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "WitchDrawer.Benchmarks",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Benchmark cleanup is best-effort and never affects the measurement.
        }
    }
}
