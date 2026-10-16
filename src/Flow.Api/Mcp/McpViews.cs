using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.CustomFields;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Mcp;

// Ответы инструментов MCP. Не DTO из Flow.Shared напрямую: те несут Id статусов, типов и людей, и модели
// пришлось бы делать по запросу на каждое имя. Здесь имена уже подставлены, а пустое (null) не сериализуется.

/// <summary>Задача глазами модели: статус, тип, люди и родитель — именами. Description — только в get_task.</summary>
public sealed record McpTask(
    Guid Id,
    string Code,
    string Title,
    string? Project,
    string? Status,
    StatusType? StatusCategory,
    bool IsDone,
    string? Type,
    TaskPriority Priority,
    string? Assignee,
    string? Creator,
    string? Description,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? StoryPoints,
    int? EstimateMinutes,
    string? Parent,
    string? Subtasks,
    string? Checklist,
    int Comments,
    int? BlockedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Url);

public sealed record McpTaskPage(int Matched, int Offset, IReadOnlyList<McpTask> Items, string? Next);

public sealed record McpTaskDetails(
    McpTask Task,
    IReadOnlyList<McpChecklistItem>? Checklist,
    IReadOnlyList<McpComment>? Comments);

public sealed record McpChecklistItem(Guid Id, string Text, bool IsDone);

public sealed record McpComment(Guid Id, string? Author, string Body, DateTime CreatedAt, DateTime? EditedAt);

public sealed record McpProjectSummary(Guid Id, string Key, string Name, int TaskCount);

public sealed record McpProject(
    Guid Id,
    string Key,
    string Name,
    int TaskCount,
    WorkflowMode WorkflowMode,
    IReadOnlyList<McpStatus> Statuses,
    IReadOnlyList<McpTaskType> TaskTypes,
    IReadOnlyList<McpCustomField>? CustomFields);

public sealed record McpStatus(string Name, StatusType? Category, bool IsInitial, bool IsFinal);

public sealed record McpTaskType(string Name, TaskTypeKind Kind, bool IsDefault);

public sealed record McpCustomField(string Key, string Name, CustomFieldType Type, bool IsRequired, IReadOnlyList<string>? Options);

public sealed record McpUser(string Username, string FullName, string Email);

public sealed record McpSearchHit(
    SearchSourceType Type,
    string Title,
    string Snippet,
    string? TaskCode,
    string? Project,
    bool IsClosed,
    DateTime UpdatedAt);

public sealed record McpSearchResult(int Total, bool Degraded, IReadOnlyList<McpSearchHit> Items);
