using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskTreeQuery;

/// <summary>
/// Дерево задач проекта (docs/TZ_task_model.md §3): плоский список в порядке обхода с глубиной. RootId — поддерево
/// одной задачи (вместе с ней). null — проекта нет, он скрыт или RootId не из него. Фильтр по дереву
/// (с «контекстными» предками) появится вместе с общим фильтром задач (2A).
/// </summary>
public sealed record TaskTreeQuery(Guid ActorId, Guid BoardId, Guid? RootId = null) : IRequest<IReadOnlyList<TaskTreeNode>?>;
