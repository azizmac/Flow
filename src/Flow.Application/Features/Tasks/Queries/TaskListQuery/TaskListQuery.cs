using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Ids;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskListQuery;

public sealed record TaskListQuery(BoardId BoardId) : IRequest<IReadOnlyList<TaskResponse>>;
