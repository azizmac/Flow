using Flow.Shared.Contracts.Tasks;

namespace Flow.Application.Features.Tasks.Commands.TaskLinkCreateCommand;

/// <summary>Задачи нет (404) | такая связь уже есть (409) | создана, возможно с предупреждением о цикле блокировок.</summary>
public sealed class TaskLinkCreateResult
{
    public bool IsNotFound { get; private init; }

    public bool IsDuplicate { get; private init; }

    public TaskLinkCreatedResponse? Response { get; private init; }

    public static TaskLinkCreateResult NotFound() => new() { IsNotFound = true };

    public static TaskLinkCreateResult Duplicate() => new() { IsDuplicate = true };

    public static TaskLinkCreateResult Success(TaskLinkCreatedResponse response) => new() { Response = response };
}
