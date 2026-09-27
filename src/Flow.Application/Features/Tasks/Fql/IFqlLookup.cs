using Flow.Domain.Entities;

namespace Flow.Application.Features.Tasks.Fql;

/// <summary>
/// Всё, что биндеру нужно знать о мире: проекты (только видимые actor'у — иначе ключ приватного проекта в запросе
/// подтвердил бы его существование), люди, задачи по коду, поддеревья и связи. Реализация — FqlLookup поверх
/// репозиториев; в тестах — фейк, и сам биндер остаётся без БД.
/// </summary>
public interface IFqlLookup
{
    Task<IReadOnlyList<Board>> VisibleBoardsAsync(CancellationToken cancellationToken);

    /// <summary>Username в нижнем регистре → Id; неизвестные в словаре отсутствуют.</summary>
    Task<IReadOnlyDictionary<string, Guid>> UsersByUsernameAsync(IReadOnlyCollection<string> usernames, CancellationToken cancellationToken);

    /// <summary>Задача по коду в видимом проекте; иначе null.</summary>
    Task<TaskItem?> TaskByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Все потомки задачи (без неё самой).</summary>
    Task<IReadOnlyList<Guid>> DescendantsAsync(TaskItem root, CancellationToken cancellationToken);

    /// <summary>Задачи, связанные с этой любой связью (оба направления).</summary>
    Task<IReadOnlyList<Guid>> LinkedAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Спринты видимых проектов, включая завершённые (docs/TZ_task_views.md §2).</summary>
    Task<IReadOnlyList<Sprint>> SprintsAsync(CancellationToken cancellationToken);

    /// <summary>Задачи, которые эта блокирует (исходящие Blocks).</summary>
    Task<IReadOnlyList<Guid>> BlockedByAsync(Guid taskId, CancellationToken cancellationToken);
}
