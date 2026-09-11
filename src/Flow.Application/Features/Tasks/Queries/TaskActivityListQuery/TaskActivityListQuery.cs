using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;

/// <summary>null — задачи нет (404). Записи по возрастанию CreatedAt.</summary>
public sealed record TaskActivityListQuery(Guid TaskId) : IRequest<IReadOnlyList<TaskActivityResponse>?>;
