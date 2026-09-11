using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using DomainActivityType = Flow.Domain.Entities.TaskActivityType;
using SharedActivityType = Flow.Shared.Contracts.Tasks.TaskActivityType;

namespace Flow.Application.Features.Tasks;

public static class TaskMappingExtensions
{
    /// <summary>commentCount — из ITaskCommentRepository.CountByTaskIdsAsync (список) или отдельного запроса; по умолчанию 0 там, где не считали.</summary>
    public static TaskResponse ToResponse(this TaskItem task, int commentCount = 0) => new(
        task.Id,
        task.BoardId,
        task.Code.Value,
        task.Title,
        task.Description,
        task.StatusId,
        task.AssigneeId,
        task.CreatedAt,
        task.CreatedById,
        task.DueDate,
        commentCount);

    public static TaskCommentResponse ToResponse(this TaskComment comment) => new(
        comment.Id,
        comment.TaskId,
        comment.AuthorId,
        comment.Body,
        comment.Mentions.Select(m => m.UserId).ToList(),
        comment.CreatedAt,
        comment.EditedAt);

    public static TaskActivityResponse ToResponse(this TaskActivity activity) => new(
        activity.Id,
        activity.TaskId,
        activity.ActorId,
        activity.Type.ToResponseType(),
        activity.OldValue,
        activity.NewValue,
        activity.CreatedAt);

    /// <summary>Значения enum'ов совпадают (зеркала, как UserRole), поэтому — приведение с проверкой.</summary>
    public static SharedActivityType ToResponseType(this DomainActivityType type) =>
        Enum.IsDefined(type) ? (SharedActivityType)(int)type : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown TaskActivityType.");
}
