using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Native.Tests;

public sealed class RustDrawerServiceTests
{
    [Fact]
    public async Task ConstructorDefersDatabaseInitializationToAsyncBoundary()
    {
        using var temp = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temp.Path, "witchdrawer.db");
        using var service = new RustDrawerService(temp.Path);

        Assert.False(File.Exists(databasePath));

        await service.InitializeAsync();

        Assert.True(File.Exists(databasePath));
        Assert.Equal(2, (await service.GetBoxesAsync()).Count);
    }

    [Fact]
    public async Task RustProductionServiceReadsAndMutatesExistingCSharpDatabase()
    {
        using var temp = new TemporaryDirectory();
        var dataRoot = System.IO.Path.Combine(temp.Path, "data");
        var paths = new AppPaths(dataRoot);
        var legacy = new DrawerService(paths, new DrawerRepository(paths.DatabasePath));
        await legacy.InitializeAsync();
        await legacy.SetSettingAsync("Theme", "Crystal");

        var normalBox = Assert.Single(await legacy.GetBoxesAsync(), box => box.Type == BoxType.Normal);
        var sourceDirectory = System.IO.Path.Combine(temp.Path, "source");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = System.IO.Path.Combine(sourceDirectory, "legacy.txt");
        await File.WriteAllTextAsync(sourcePath, "legacy-content");
        var legacyItem = await legacy.ImportPathAsync(normalBox.Id, sourcePath);

        using var rust = new RustDrawerService(dataRoot);
        await rust.InitializeAsync();

        Assert.Equal("Crystal", await rust.GetSettingAsync("Theme"));
        var migratedItem = Assert.Single(await rust.GetItemsAsync(normalBox.Id));
        Assert.Equal(legacyItem.Id, migratedItem.Id);
        Assert.Equal(legacyItem.StoredPath, migratedItem.StoredPath);

        var deleteResult = await rust.DeleteItemAsync(migratedItem.Id);
        Assert.True(deleteResult.RestoredToOriginal);
        Assert.Equal("legacy-content", await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public void NewContextInitializesDefaultsAndRoundTripsUtf8()
    {
        using var temp = new TemporaryDirectory();
        using var service = new RustDrawerService(temp.Path);

        var defaults = service.GetBoxes();
        var created = service.CreateBox("中文收纳盒", BoxType.Mapping);
        var boxes = service.GetBoxes();

        Assert.Equal(2, defaults.Count);
        Assert.Contains(defaults, box => box.Type == BoxType.Normal);
        Assert.Contains(defaults, box => box.Type == BoxType.Mapping);
        Assert.Equal(BoxType.Mapping, created.Type);
        Assert.Contains(boxes, box => box.Id == created.Id && box.Name == "中文收纳盒");
    }

    [Fact]
    public void ImportAndDeleteRestoresFileThroughNativeBoundary()
    {
        using var temp = new TemporaryDirectory();
        using var service = new RustDrawerService(System.IO.Path.Combine(temp.Path, "data"));
        var normalBox = Assert.Single(service.GetBoxes(), box => box.Type == BoxType.Normal);
        var sourceDirectory = System.IO.Path.Combine(temp.Path, "source");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = System.IO.Path.Combine(sourceDirectory, "重要.txt");
        File.WriteAllText(sourcePath, "content");

        var imported = service.ImportPath(normalBox.Id, sourcePath);

        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(imported.StoredPath));

        var result = service.DeleteItem(imported.Id);

        Assert.True(result.RestoredToOriginal);
        Assert.False(result.RestoredToDesktop);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal("content", File.ReadAllText(sourcePath));
    }

    [Fact]
    public void TodoServiceSharesOwnedContextAndRoundTripsUtf8()
    {
        using var temp = new TemporaryDirectory();
        using var drawerService = new RustDrawerService(temp.Path);
        var todoBox = drawerService.CreateBox("待办", BoxType.Todo);
        var todoService = new RustTodoService(drawerService);

        var todo = todoService.AddTodo(todoBox.Id, "检查桥接层");

        Assert.Equal("检查桥接层", todo.Title);
        Assert.Contains(todoService.GetTodos(todoBox.Id), item => item.Id == todo.Id);
    }

    [Fact]
    public void TodoTitleLimitCountsUnicodeCharacters()
    {
        using var temp = new TemporaryDirectory();
        using var drawerService = new RustDrawerService(temp.Path);
        var todoBox = drawerService.CreateBox("待办", BoxType.Todo);
        var todoService = new RustTodoService(drawerService);
        var title = string.Concat(Enumerable.Repeat("待", 200));

        var todo = todoService.AddTodo(todoBox.Id, title);

        Assert.Equal(200, todo.Title.Length);
    }

    [Fact]
    public void CallsAfterDisposeAreRejected()
    {
        using var temp = new TemporaryDirectory();
        var service = new RustDrawerService(temp.Path);
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.GetBoxes());
    }

    [Fact]
    public async Task AsyncProductionContractsPersistSettingsAndArchiveTodos()
    {
        using var temp = new TemporaryDirectory();
        using var owner = new RustDrawerService(temp.Path);
        IDrawerService drawer = owner;
        ITodoService todos = new RustTodoService(owner);

        await drawer.SetSettingAsync("Theme", "Crystal");
        var todoBox = await drawer.CreateBoxAsync("待办", BoxType.Todo);
        var first = await todos.AddTodoAsync(todoBox.Id, "第一项");
        _ = await todos.AddTodoAsync(todoBox.Id, "第二项");
        await todos.SetCompletedAsync(first.Id, true);

        var archivedCount = await todos.ArchiveCompletedAsync(todoBox.Id);
        var archived = await todos.GetArchivedTodosAsync(todoBox.Id);

        Assert.Equal("Crystal", await drawer.GetSettingAsync("Theme"));
        Assert.Equal(1, archivedCount);
        var archivedTodo = Assert.Single(archived);
        Assert.Equal(first.Id, archivedTodo.Id);

        var restored = await todos.RestoreArchivedAsync(first.Id);
        Assert.False(restored.IsArchived);
        Assert.Empty(await todos.GetArchivedTodosAsync(todoBox.Id));
    }

    [Fact]
    public async Task UpdateContractRejectsUntrustedDownloadWithoutNetworkAccess()
    {
        using var temp = new TemporaryDirectory();
        using var owner = new RustDrawerService(temp.Path);
        IUpdateService updates = new RustUpdateService(owner);

        var applied = await updates.DownloadAndApplyUpdateAsync("http://example.com/update.zip");

        Assert.False(applied);
    }

    [Fact]
    public async Task ProductionDrawerContractCoversMoveSearchOpenExportReorderAndDeleteBox()
    {
        using var temp = new TemporaryDirectory();
        using var owner = new RustDrawerService(System.IO.Path.Combine(temp.Path, "data"));
        IDrawerService drawer = owner;
        var defaults = await drawer.GetBoxesAsync();
        var sourceBox = Assert.Single(defaults, box => box.Type == BoxType.Normal);
        var targetBox = await drawer.CreateBoxAsync("目标盒", BoxType.Normal);
        var sourceDirectory = System.IO.Path.Combine(temp.Path, "source");
        Directory.CreateDirectory(sourceDirectory);
        var firstSource = System.IO.Path.Combine(sourceDirectory, "move-me.txt");
        await File.WriteAllTextAsync(firstSource, "move-content");

        var imported = await drawer.ImportPathAsync(sourceBox.Id, firstSource, 1, 2);
        await drawer.UpdateItemGridPositionAsync(imported.Id, 3, 4);
        await drawer.MoveItemToBoxAsync(imported.Id, targetBox.Id, 5, 6);

        var moved = Assert.Single(await drawer.GetItemsAsync(targetBox.Id));
        Assert.Equal((5, 6), (moved.GridColumn, moved.GridRow));
        Assert.Contains(await drawer.SearchItemsAsync("move-me"), item => item.Id == moved.Id);

        var launcher = new RecordingFileLauncher();
        await drawer.OpenItemAsync(moved.Id, launcher);
        Assert.Equal(moved.EffectivePath, launcher.LastOpenedPath);

        var exportDirectory = System.IO.Path.Combine(temp.Path, "export");
        var exportedPath = await drawer.ExportItemToDirectoryAsync(moved.Id, exportDirectory);
        Assert.Equal("move-content", await File.ReadAllTextAsync(exportedPath));
        Assert.Empty(await drawer.GetItemsAsync(targetBox.Id));

        var secondSource = System.IO.Path.Combine(sourceDirectory, "restore-on-box-delete.txt");
        await File.WriteAllTextAsync(secondSource, "restore-content");
        _ = await drawer.ImportPathAsync(targetBox.Id, secondSource);

        var reversedIds = (await drawer.GetBoxesAsync()).Reverse().Select(box => box.Id).ToArray();
        await drawer.ReorderBoxesAsync(reversedIds);
        Assert.Equal(reversedIds, (await drawer.GetBoxesAsync()).Select(box => box.Id));

        var deleteResult = await drawer.DeleteBoxAsync(targetBox.Id);
        Assert.True(deleteResult.BoxRemoved);
        Assert.Equal("restore-content", await File.ReadAllTextAsync(secondSource));
    }

    [Fact]
    public async Task SearchLimitIsPassedThroughNativeBoundary()
    {
        using var temp = new TemporaryDirectory();
        using var owner = new RustDrawerService(System.IO.Path.Combine(temp.Path, "data"));
        IDrawerService drawer = owner;
        var mappingBox = Assert.Single(await drawer.GetBoxesAsync(), box => box.Type == BoxType.Mapping);
        var sourcePath = System.IO.Path.Combine(temp.Path, "match.txt");
        await File.WriteAllTextAsync(sourcePath, "content");

        for (var index = 0; index < 205; index++)
        {
            await drawer.ImportPathAsync(mappingBox.Id, sourcePath);
        }

        Assert.Equal(205, (await drawer.SearchItemsAsync("match", 250)).Count);
        Assert.Equal(3, (await drawer.SearchItemsAsync("match", 3)).Count);
    }

    [Fact]
    public async Task ConcurrentTodoWritesAreSerializedByProductionAdapter()
    {
        using var temp = new TemporaryDirectory();
        using var owner = new RustDrawerService(temp.Path);
        IDrawerService drawer = owner;
        ITodoService todos = new RustTodoService(owner);
        var todoBox = await drawer.CreateBoxAsync("并发待办", BoxType.Todo);

        await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(index => todos.AddTodoAsync(todoBox.Id, $"Todo {index}")));

        Assert.Equal(20, (await todos.GetTodosAsync(todoBox.Id)).Count);
    }

    private sealed class RecordingFileLauncher : IFileLauncher
    {
        public string? LastOpenedPath { get; private set; }

        public Task OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastOpenedPath = path;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "WitchDrawer.Core.Native.Tests",
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
                // A failed test may leave SQLite handles briefly alive; temp cleanup is best-effort.
            }
        }
    }
}
