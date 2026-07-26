using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class TodoServiceTests
{
    [Fact]
    public async Task AddTodoAsync_PersistsTrimmedTitleAndSortOrder()
    {
        using var workspace = await TodoWorkspace.CreateAsync();

        var first = await workspace.Service.AddTodoAsync("  first task  ");
        var second = await workspace.Service.AddTodoAsync("second task");

        var reloadedService = new TodoService(new DrawerRepository(workspace.DatabasePath));
        var todos = await reloadedService.GetTodosAsync();

        Assert.Collection(
            todos,
            item =>
            {
                Assert.Equal(first.Id, item.Id);
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

        await Assert.ThrowsAsync<ArgumentException>(() => workspace.Service.AddTodoAsync("   "));
        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.AddTodoAsync(new string('x', TodoService.MaximumTitleLength + 1)));

        Assert.Empty(await workspace.Service.GetTodosAsync());
    }

    [Fact]
    public async Task SetCompletedAsync_UpdatesStateAndMovesCompletedItemAfterActiveItems()
    {
        using var workspace = await TodoWorkspace.CreateAsync();

        var first = await workspace.Service.AddTodoAsync("first");
        var second = await workspace.Service.AddTodoAsync("second");

        var completed = await workspace.Service.SetCompletedAsync(first.Id, isCompleted: true);
        var afterCompletion = await workspace.Service.GetTodosAsync();

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

        var kept = await workspace.Service.AddTodoAsync("keep");
        var removed = await workspace.Service.AddTodoAsync("remove");

        await workspace.Service.DeleteTodoAsync(removed.Id);

        var remaining = Assert.Single(await workspace.Service.GetTodosAsync());
        Assert.Equal(kept.Id, remaining.Id);
    }

    private sealed class TodoWorkspace : IDisposable
    {
        private TodoWorkspace(string root, string databasePath, TodoService service)
        {
            Root = root;
            DatabasePath = databasePath;
            Service = service;
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public TodoService Service { get; }

        public static async Task<TodoWorkspace> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.TodoTests", Guid.NewGuid().ToString("N"));
            var databasePath = Path.Combine(root, "witchdrawer.db");
            var repository = new DrawerRepository(databasePath);
            await repository.InitializeAsync();

            return new TodoWorkspace(root, databasePath, new TodoService(repository));
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
