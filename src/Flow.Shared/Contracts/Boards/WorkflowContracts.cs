namespace Flow.Shared.Contracts.Boards;

/// <summary>Зеркало Flow.Domain.Entities.WorkflowMode: Free — любой статус, Restricted — только по графу.</summary>
public enum WorkflowMode
{
    Free = 0,
    Restricted = 1
}

/// <summary>Условия перехода — выполняться должны все (docs/TZ_workflow_config.md §2).</summary>
public sealed record TransitionConditionsDto(
    ProjectRole? MinRole = null,
    bool RequireAssignee = false,
    bool RequireChildrenDone = false,
    bool RequireChecklistDone = false);

/// <summary>Переход графа; FromStatusId = null — «из любого статуса».</summary>
public sealed record TransitionResponse(Guid Id, Guid? FromStatusId, Guid ToStatusId, string? Name, TransitionConditionsDto Conditions);

/// <summary>
/// Workflow проекта. DeadEnds — нефинальные статусы без исходящих переходов: в Restricted их не бывает
/// (сохранение с тупиками отклоняется), в Free — подсказка, что поправить перед включением.
/// </summary>
public sealed record WorkflowResponse(Guid BoardId, WorkflowMode Mode, IReadOnlyList<TransitionResponse> Transitions, IReadOnlyList<Guid> DeadEnds);

public sealed record TransitionRequest(Guid? FromStatusId, Guid ToStatusId, string? Name = null, TransitionConditionsDto? Conditions = null);

/// <summary>Workflow заменяется целиком: режим и все переходы.</summary>
public sealed record SetWorkflowRequest(WorkflowMode Mode, IReadOnlyList<TransitionRequest> Transitions);

/// <summary>Куда можно перевести задачу: по каждому статусу проекта (кроме текущего) — можно ли и почему нет.</summary>
public sealed record TaskTransitionResponse(Guid StatusId, bool Allowed, IReadOnlyList<string> Reasons);
