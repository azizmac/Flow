using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;

/// <summary>null — задачи нет (404). Читать могут все роли.</summary>
public sealed record TaskCommentListQuery(Guid TaskId) : IRequest<IReadOnlyList<TaskCommentResponse>?>;
