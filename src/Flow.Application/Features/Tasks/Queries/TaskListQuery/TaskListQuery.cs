using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskListQuery;

public sealed record TaskListQuery(Guid BoardId) : IRequest<IReadOnlyList<TaskResponse>>;
