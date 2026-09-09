using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskListQuery;

/// <summary>AssigneeId — необязательный фильтр «задачи этого исполнителя» в рамках доски.</summary>
public sealed record TaskListQuery(Guid BoardId, Guid? AssigneeId = null) : IRequest<IReadOnlyList<TaskResponse>>;
