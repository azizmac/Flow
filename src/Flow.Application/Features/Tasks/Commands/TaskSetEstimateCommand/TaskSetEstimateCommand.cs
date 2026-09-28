using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;

/// <summary>
/// Story points и оценка времени (минуты) вместе, null — снять. Права — как на редактирование задачи;
/// значение вне диапазона — 400 (см. TaskItem.SetStoryPoints/SetEstimate).
/// </summary>
public sealed record TaskSetEstimateCommand(Guid ActorId, Guid TaskId, decimal? StoryPoints, int? EstimateMinutes)
    : IRequest<TaskUpdateResult>;
