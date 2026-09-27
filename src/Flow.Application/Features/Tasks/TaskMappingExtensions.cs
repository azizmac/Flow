using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using DomainActivityType = Flow.Domain.Entities.TaskActivityType;
using DomainPriority = Flow.Domain.Entities.TaskPriority;
using SharedPriority = Flow.Shared.Contracts.Tasks.TaskPriority;
using SharedActivityType = Flow.Shared.Contracts.Tasks.TaskActivityType;
using DomainLinkType = Flow.Domain.Entities.TaskLinkType;
using SharedLinkType = Flow.Shared.Contracts.Tasks.TaskLinkType;

namespace Flow.Application.Features.Tasks;

public static class TaskMappingExtensions
{
    /// <summary>commentCount — из ITaskCommentRepository.CountByTaskIdsAsync (список) или отдельного запроса; по умолчанию 0 там, где не считали.</summary>
    public static TaskResponse ToResponse(this TaskItem task, int commentCount = 0, ChildCounts children = default, int blockedByCount = 0) => new(
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
        commentCount,
        task.TypeId,
        task.Priority.ToResponsePriority(),
        task.StartDate,
        task.StoryPoints,
        task.EstimateMinutes,
        task.UpdatedAt,
        task.ParentId,
        children.Total,
        children.Done,
        blockedByCount,
        task.ChecklistDone,
        task.ChecklistTotal,
        task.SprintId,
        task.MilestoneId);

    public static TaskChecklistItemResponse ToResponse(this TaskChecklistItem item) =>
        new(item.Id, item.Text, item.IsDone, item.DoneAt, item.DoneById);

    public static IReadOnlyList<TaskChecklistItemResponse> ChecklistResponse(this TaskItem task) =>
        task.Checklist.Select(i => i.ToResponse()).ToList();

    public static SharedLinkType ToResponseLinkType(this DomainLinkType type) =>
        Enum.IsDefined(type) ? (SharedLinkType)(int)type : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown TaskLinkType.");

    public static DomainLinkType ToDomainLinkType(this SharedLinkType type) =>
        Enum.IsDefined(type) ? (DomainLinkType)(int)type : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown TaskLinkType.");

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

    /// <summary>Зеркала с одинаковыми значениями — приведение с проверкой, как у TaskActivityType.</summary>
    public static SharedPriority ToResponsePriority(this DomainPriority priority) =>
        Enum.IsDefined(priority) ? (SharedPriority)(int)priority : throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown TaskPriority.");

    /// <summary>Обратное направление — для фильтра GET /tasks; неизвестное значение из query-string — 400 в контроллере.</summary>
    public static DomainPriority ToDomainPriority(this SharedPriority priority) =>
        Enum.IsDefined(priority) ? (DomainPriority)(int)priority : throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown TaskPriority.");

    /// <summary>Значения enum'ов совпадают (зеркала, как UserRole), поэтому — приведение с проверкой.</summary>
    public static SharedActivityType ToResponseType(this DomainActivityType type) =>
        Enum.IsDefined(type) ? (SharedActivityType)(int)type : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown TaskActivityType.");
}
