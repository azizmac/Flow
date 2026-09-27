using Flow.Application.Security;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskGetQuery;

public sealed record TaskGetQuery(Guid ActorId, Guid TaskId) : IRequest<TaskResponse?>;
