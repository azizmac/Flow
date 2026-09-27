namespace Flow.Shared.Contracts.Boards;

/// <summary>
/// TaskCount и NextTaskNumber нужны карточке проекта в клиенте («12 задач», «следующая FRONT-13»),
/// чтобы не делать N+1 запросов GET /boards/{id}/tasks ради счётчика. TaskTypes — типы задач проекта,
/// включая архивные (docs/TZ_task_model.md §1). DefaultRole — потолок роли в проекте без участия
/// (docs/TZ_project_access.md), null — без ограничения. WorkflowMode — Restricted: статус меняется только
/// по переходам (клиент тогда спрашивает GET /tasks/{id}/transitions перед показом списка статусов).
/// </summary>
public sealed record BoardResponse(
    Guid Id,
    string Key,
    string Name,
    DateTime CreatedAt,
    int TaskCount,
    int NextTaskNumber,
    IReadOnlyList<StatusResponse> Statuses,
    IReadOnlyList<TaskTypeResponse> TaskTypes,
    ProjectRole? DefaultRole,
    BoardVisibility Visibility,
    WorkflowMode WorkflowMode = WorkflowMode.Free);
