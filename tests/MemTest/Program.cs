using System.Diagnostics;
using System.Text.Json;
using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

static double GetRssMb()
{
    using var proc = Process.GetCurrentProcess();
    return proc.WorkingSet64 / (1024.0 * 1024.0);
}

var tmpDir = Path.Combine(Path.GetTempPath(), "witchdrawer_memtest_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmpDir);
var dbPath = Path.Combine(tmpDir, "witchdrawer.db");

Console.WriteLine("=== WitchDrawer .NET Core Memory Benchmark ===\n");

var rss0 = GetRssMb();
Console.WriteLine($"Phase 0 - Process baseline:           {rss0:F2} MB");

// Phase 1: SQLite init
var repo = new DrawerRepository(dbPath);
await repo.InitializeAsync();
var rss1 = GetRssMb();
Console.WriteLine($"Phase 1 - After SQLite init:          {rss1:F2} MB  (+{rss1 - rss1:F2})");

// Phase 2: 100 boxes
var boxIds = new List<Guid>();
for (int i = 0; i < 100; i++)
{
    var id = Guid.NewGuid();
    var box = new Box(id, $"Box {i}", BoxType.Normal, null, i, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    await repo.AddBoxAsync(box);
    boxIds.Add(id);
}
var rss2 = GetRssMb();
Console.WriteLine($"Phase 2 - After 100 boxes:            {rss2:F2} MB  (+{rss2 - rss1:F2})");

// Phase 3: 500 items
var mainBox = boxIds[0];
for (int i = 0; i < 500; i++)
{
    var item = new DrawerItem(
        Guid.NewGuid(), mainBox, $"file_{i}.txt", ItemKind.File,
        $"C:\\Users\\test\\file_{i}.txt", null, i,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    await repo.AddItemAsync(item);
}
var rss3 = GetRssMb();
Console.WriteLine($"Phase 3 - After 500 items:            {rss3:F2} MB  (+{rss3 - rss2:F2})");

// Phase 4: 200 todos
var todoBoxId = Guid.NewGuid();
await repo.AddBoxAsync(new Box(todoBoxId, "Todos", BoxType.Todo, null, 999, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
for (int i = 0; i < 200; i++)
{
    await repo.AddTodoAsync(new TodoItem(
        Guid.NewGuid(), todoBoxId, $"Task {i}", i % 3 == 0, i,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
}
var rss4 = GetRssMb();
Console.WriteLine($"Phase 4 - After 200 todos:            {rss4:F2} MB  (+{rss4 - rss3:F2})");

// Phase 5: Read all into memory
var allBoxes = await repo.GetBoxesAsync();
var allItems = await repo.GetItemsAsync();
var allTodos = await repo.GetTodosAsync(todoBoxId);
var rss5 = GetRssMb();
Console.WriteLine($"Phase 5 - After read all into List:   {rss5:F2} MB  (+{rss5 - rss4:F2})");

// Phase 6: JSON serialize
var jsonBoxes = JsonSerializer.Serialize(allBoxes);
var jsonItems = JsonSerializer.Serialize(allItems);
var rss6 = GetRssMb();
Console.WriteLine($"Phase 6 - After JSON serialize:       {rss6:F2} MB  (+{rss6 - rss5:F2})");
Console.WriteLine($"          JSON boxes len: {jsonBoxes.Length} bytes, items len: {jsonItems.Length} bytes");

// Phase 7: Service layer
var svcRepo = new DrawerRepository(Path.Combine(tmpDir, "svc.db"));
await svcRepo.InitializeAsync();
var paths = new AppPaths(tmpDir);
var svc = new DrawerService(paths, svcRepo);
await svc.InitializeAsync();
var svcBoxes = await svc.GetBoxesAsync();
var rss7 = GetRssMb();
Console.WriteLine($"Phase 7 - After service layer:        {rss7:F2} MB  (+{rss7 - rss6:F2})");

// Phase 8: Stress - 1000 queries
var sw = Stopwatch.StartNew();
for (int i = 0; i < 1000; i++)
{
    var _ = await repo.GetBoxesAsync();
    var __ = await repo.GetItemsAsync(mainBox);
    var ___ = await repo.SearchItemsAsync("file_42", 200);
}
sw.Stop();
var rss8 = GetRssMb();
Console.WriteLine($"Phase 8 - After 3000 queries:         {rss8:F2} MB  (+{rss8 - rss7:F2})  [{sw.ElapsedMilliseconds} ms]");

Console.WriteLine($"\nFinal RSS: {rss8:F2} MB");
Console.WriteLine($"Total growth: {rss8 - rss0:F2} MB (from {rss0:F2} to {rss8:F2})");

// Cleanup
try { Directory.Delete(tmpDir, true); } catch { }
