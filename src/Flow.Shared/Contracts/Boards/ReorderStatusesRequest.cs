namespace Flow.Shared.Contracts.Boards;

/// <summary>Полный список Id статусов проекта в новом порядке; неполный или с чужими Id — 400.</summary>
public sealed record ReorderStatusesRequest(IReadOnlyList<Guid> StatusIds);
