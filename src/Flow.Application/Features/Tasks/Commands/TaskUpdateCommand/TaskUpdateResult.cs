using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Ids;

namespace Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;

/// <summary>
/// Три исхода обновления задачи различаются HTTP-ответом в контроллере (404 / 400 / 200),
/// поэтому вместо исключения для "статус с другой доски" используется явный результат.
/// </summary>
public sealed class TaskUpdateResult
{
    public bool IsNotFound { get; }

    public string? ValidationError { get; }

    public TaskResponse? Response { get; }

    private TaskUpdateResult(bool isNotFound, string? validationError, TaskResponse? response)
    {
        IsNotFound = isNotFound;
        ValidationError = validationError;
        Response = response;
    }

    public static TaskUpdateResult NotFound() => new(true, null, null);

    public static TaskUpdateResult InvalidStatus(StatusId statusId) =>
        new(false, $"Status {statusId} does not belong to task's board.", null);

    public static TaskUpdateResult Success(TaskResponse response) => new(false, null, response);
}
