using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskTreeQuery;

/// <summary>
/// Дерево задач проекта (docs/TZ_task_model.md §3, docs/TZ_task_views.md §3): плоский список в порядке обхода с глубиной.
/// RootId — поддерево одной задачи (вместе с ней). Фильтры — те же, что у списка (панель и FQL; ORDER BY из FQL
/// не действует — порядок задаёт ранг): подходящие узлы видны как есть, их предки — «контекстом» (IsContextOnly),
/// ветки без подходящих задач скрыты. MaxDepth — глубина от корня обхода, до которой узлы отдаются (0 — только
/// верхний уровень); глубже клиент догружает по раскрытию, запрашивая поддерево узла. Прогресс — по всему поддереву
/// без фильтра. null — проекта нет, он скрыт или RootId не из него.
/// </summary>
public sealed record TaskTreeQuery(
    Guid ActorId,
    Guid BoardId,
    Guid? RootId = null,
    Guid? AssigneeId = null,
    bool Unassigned = false,
    string? Query = null,
    TaskTypeKind? TypeKind = null,
    TaskPriority? Priority = null,
    Guid? StatusId = null,
    string? Fql = null,
    int? MaxDepth = null) : IRequest<IReadOnlyList<TaskTreeNode>?>;
