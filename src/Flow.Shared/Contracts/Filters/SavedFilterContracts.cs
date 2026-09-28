namespace Flow.Shared.Contracts.Filters;

/// <summary>
/// Сохранённый фильтр (docs/TZ_task_views.md §7). Shared — виден всем, но результат каждому считается его правами.
/// IsOwner/IsStarred — с точки зрения того, кто спросил.
/// </summary>
public sealed record SavedFilterResponse(
    Guid Id,
    string Name,
    string Query,
    string View,
    bool Shared,
    Guid OwnerId,
    bool IsOwner,
    bool IsStarred,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Query проверяется биндингом FQL: ошибка — 400 с позицией, как у GET /tasks?fql=.</summary>
public sealed record CreateSavedFilterRequest(string Name, string Query, bool Shared = false);

/// <summary>PATCH-семантика: null — не трогать.</summary>
public sealed record UpdateSavedFilterRequest(string? Name = null, string? Query = null, bool? Shared = null);
