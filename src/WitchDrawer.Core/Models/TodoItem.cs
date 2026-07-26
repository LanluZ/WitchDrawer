namespace WitchDrawer.Core.Models;

public sealed record TodoItem(
    Guid Id,
    string Title,
    bool IsCompleted,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt = null);
