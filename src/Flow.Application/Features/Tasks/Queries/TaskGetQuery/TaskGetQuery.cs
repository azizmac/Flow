using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Ids;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskGetQuery;

public sealed record TaskGetQuery(TaskId TaskId) : IRequest<TaskResponse?>;
