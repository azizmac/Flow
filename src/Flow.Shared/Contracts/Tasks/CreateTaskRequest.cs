namespace Flow.Shared.Contracts.Tasks;

/// <summary>TemplateId — задача по шаблону (этап 3G): чек-лист и подзадачи шаблона создаются вместе с ней.</summary>
/// <summary>TypeId = null — тип проекта по умолчанию; Priority = null — None; ParentId — сразу подзадачей (тот же проект, тип ниже родителя).</summary>
public sealed record CreateTaskRequest(string Title, string? Description, Guid? StatusId, Guid? TypeId = null, TaskPriority? Priority = null, Guid? ParentId = null,
    IReadOnlyDictionary<Guid, System.Text.Json.JsonElement?>? CustomFields = null,
    Guid? AssigneeId = null,
    Guid? TemplateId = null);

/// <summary>Команда задачи (этап 4D): группа-команда или null — снять.</summary>
public sealed record SetTaskTeamRequest(Guid? TeamId);
