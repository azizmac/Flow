using Flow.Shared.Contracts.Tasks;

namespace Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;

/// <summary>Задача или комментарий не найдены → 404; иначе 200/201 с комментарием. Общий для добавления и правки.</summary>
public sealed class TaskCommentResult
{
    public bool IsNotFound { get; }

    public TaskCommentResponse? Response { get; }

    private TaskCommentResult(bool isNotFound, TaskCommentResponse? response)
    {
        IsNotFound = isNotFound;
        Response = response;
    }

    public static TaskCommentResult NotFound() => new(true, null);

    public static TaskCommentResult Success(TaskCommentResponse response) => new(false, response);
}
