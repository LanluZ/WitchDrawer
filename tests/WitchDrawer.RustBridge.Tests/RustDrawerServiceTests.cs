using WitchDrawer.Core.Models;
using WitchDrawer.RustBridge;

namespace WitchDrawer.RustBridge.Tests;

public sealed class RustDrawerServiceTests
{
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
    public void CallsAfterDisposeAreRejected()
    {
        using var temp = new TemporaryDirectory();
        var service = new RustDrawerService(temp.Path);
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.GetBoxes());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "WitchDrawer.RustBridge.Tests",
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
