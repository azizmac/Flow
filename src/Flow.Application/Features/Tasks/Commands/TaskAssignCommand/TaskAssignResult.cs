using Flow.Shared.Contracts.Tasks;

namespace Flow.Application.Features.Tasks.Commands.TaskAssignCommand;

/// <summary>
/// Задача не найдена → 404; пользователь не найден или деактивирован → 400 (ValidationError); иначе 200.
/// Явный результат вместо исключений — по аналогии с TaskUpdateResult.
/// </summary>
public sealed class TaskAssignResult
{
    public bool IsNotFound { get; }

    public string? ValidationError { get; }

    public TaskResponse? Response { get; }

    private TaskAssignResult(bool isNotFound, string? validationError, TaskResponse? response)
    {
        IsNotFound = isNotFound;
        ValidationError = validationError;
        Response = response;
    }

    public static TaskAssignResult NotFound() => new(true, null, null);

    public static TaskAssignResult UserNotFound(Guid userId) =>
        new(false, $"User {userId} does not exist.", null);

    public static TaskAssignResult UserInactive(Guid userId) =>
        new(false, $"User {userId} is deactivated and cannot be assigned.", null);

    public static TaskAssignResult Success(TaskResponse response) => new(false, null, response);
}
