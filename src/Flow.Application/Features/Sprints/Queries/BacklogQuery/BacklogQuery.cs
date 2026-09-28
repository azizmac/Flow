using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Sprints;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Sprints.Queries.BacklogQuery;

/// <summary>
/// Бэклог проекта (docs/TZ_task_views.md §2): секции — активный спринт, запланированные по SortOrder, затем «Бэклог»
/// (задачи без спринта в нефинальных статусах); внутри секции — ручной порядок (Rank). Фильтры — как у списка
/// (ORDER BY из FQL не действует), EpicId — поддерево эпика. У каждой задачи — её эпик (предок вида Epic), у секции —
/// суммы и распределение по исполнителям (по отфильтрованным задачам). null — проект скрыт или его нет.
/// </summary>
public sealed record BacklogQuery(
    Guid ActorId,
    Guid BoardId,
    Guid? AssigneeId = null,
    bool Unassigned = false,
    string? Query = null,
    TaskTypeKind? TypeKind = null,
    TaskPriority? Priority = null,
    string? Fql = null,
    Guid? EpicId = null) : IRequest<BacklogResponse?>;
