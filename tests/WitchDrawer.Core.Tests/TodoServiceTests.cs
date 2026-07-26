using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class TodoServiceTests
{
    [Fact]
    public async Task AddTodoAsync_PersistsTrimmedTitleAndSortOrder()
    {
        using var workspace = await TodoWorkspace.CreateAsync();

        var first = await workspace.Service.AddTodoAsync(workspace.BoxId, "  first task  ");
        var second = await workspace.Service.AddTodoAsync(workspace.BoxId, "second task");

        var reloadedService = new TodoService(new DrawerRepository(workspace.DatabasePath));
        var todos = await reloadedService.GetTodosAsync(workspace.BoxId);

        Assert.Collection(
            todos,
            item =>
            {
                Assert.Equal(first.Id, item.Id);
                Assert.Equal(workspace.BoxId, item.BoxId);
                Assert.Equal("first task", item.Title);
                Assert.Equal(0, item.SortOrder);
                Assert.False(item.IsCompleted);
            },
            item =>
            {
                Assert.Equal(second.Id, item.Id);
                Assert.Equal(1, item.SortOrder);
            });
    }

    [Fact]
    public async Task AddTodoAsync_RejectsEmptyAndOverlongTitles()
    {
        using var workspace = await TodoWorkspace.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => workspace.Service.AddTodoAsync(workspace.BoxId, "   "));
        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.AddTodoAsync(
                workspace.BoxId,
                new string('x', TodoService.MaximumTitleLength + 1)));

        Assert.Empty(await workspace.Service.GetTodosAsync(workspace.BoxId));
    }

    [Fact]
    public async Task SetCompletedAsync_UpdatesStateAndMovesCompletedItemAfterActiveItems()
    {
        using var workspace = await TodoWorkspace.CreateAsync();

        var first = await workspace.Service.AddTodoAsync(workspace.BoxId, "first");
        var second = await workspace.Service.AddTodoAsync(workspace.BoxId, "second");

        var completed = await workspace.Service.SetCompletedAsync(first.Id, isCompleted: true);
        var afterCompletion = await workspace.Service.GetTodosAsync(workspace.BoxId);

        Assert.True(completed.IsCompleted);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal([second.Id, first.Id], afterCompletion.Select(item => item.Id));

        var reopened = await workspace.Service.SetCompletedAsync(first.Id, isCompleted: false);

        Assert.False(reopened.IsCompleted);
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task DeleteTodoAsync_RemovesOnlyRequestedItem()
    {
        using var workspace = await TodoWorkspace.CreateAsync();

        var kept = await workspace.Service.AddTodoAsync(workspace.BoxId, "keep");
        var removed = await workspace.Service.AddTodoAsync(workspace.BoxId, "remove");

        await workspace.Service.DeleteTodoAsync(removed.Id);

        var remaining = Assert.Single(await workspace.Service.GetTodosAsync(workspace.BoxId));
        Assert.Equal(kept.Id, remaining.Id);
    }

    [Fact]
    public async Task TodoBoxes_KeepIndependentListsAndCascadeOnBoxDelete()
    {
        using var workspace = await TodoWorkspace.CreateAsync();
        var secondBox = await workspace.DrawerService.CreateBoxAsync("second todo", BoxType.Todo);

        await workspace.Service.AddTodoAsync(workspace.BoxId, "first box task");
        await workspace.Service.AddTodoAsync(secondBox.Id, "second box task");

        Assert.Single(await workspace.Service.GetTodosAsync(workspace.BoxId));
        Assert.Single(await workspace.Service.GetTodosAsync(secondBox.Id));

        await workspace.DrawerService.DeleteBoxAsync(workspace.BoxId);

        Assert.Empty(await workspace.Service.GetTodosAsync(workspace.BoxId));
        Assert.Single(await workspace.Service.GetTodosAsync(secondBox.Id));
    }

    private sealed class TodoWorkspace : IDisposable
    {
        private TodoWorkspace(
            string root,
            string databasePath,
            Guid boxId,
            TodoService service,
            DrawerService drawerService)
        {
            Root = root;
            DatabasePath = databasePath;
            BoxId = boxId;
            Service = service;
            DrawerService = drawerService;
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public Guid BoxId { get; }

        public TodoService Service { get; }

        public DrawerService DrawerService { get; }

        public static async Task<TodoWorkspace> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.TodoTests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var databasePath = paths.DatabasePath;
            var repository = new DrawerRepository(databasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var todoBox = await drawerService.CreateBoxAsync("todo", BoxType.Todo);

            return new TodoWorkspace(
                root,
                databasePath,
                todoBox.Id,
                new TodoService(repository),
                drawerService);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Temp cleanup should not hide the test result.
            }
        }
    }
}
