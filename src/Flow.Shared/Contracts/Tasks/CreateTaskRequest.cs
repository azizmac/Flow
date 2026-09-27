namespace Flow.Shared.Contracts.Tasks;

/// <summary>TypeId = null — тип проекта по умолчанию; Priority = null — None; ParentId — сразу подзадачей (тот же проект, тип ниже родителя).</summary>
public sealed record CreateTaskRequest(string Title, string? Description, Guid? StatusId, Guid? TypeId = null, TaskPriority? Priority = null, Guid? ParentId = null,
    IReadOnlyDictionary<Guid, System.Text.Json.JsonElement?>? CustomFields = null,
    Guid? AssigneeId = null);
