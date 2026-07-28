using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Abstractions;

public interface ITodoService
{
    Task<IReadOnlyList<TodoItem>> GetTodosAsync(Guid boxId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TodoItem>> GetArchivedTodosAsync(Guid? boxId = null, CancellationToken cancellationToken = default);
    Task<TodoItem> AddTodoAsync(Guid boxId, string title, CancellationToken cancellationToken = default);
    Task<TodoItem> SetCompletedAsync(Guid todoId, bool isCompleted, CancellationToken cancellationToken = default);
    Task DeleteTodoAsync(Guid todoId, CancellationToken cancellationToken = default);
    Task<int> ArchiveCompletedAsync(Guid boxId, CancellationToken cancellationToken = default);
    Task<TodoItem> RestoreArchivedAsync(Guid todoId, CancellationToken cancellationToken = default);
}
