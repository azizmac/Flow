using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskGetQuery;

public sealed record TaskGetQuery(Guid TaskId) : IRequest<TaskResponse?>;
