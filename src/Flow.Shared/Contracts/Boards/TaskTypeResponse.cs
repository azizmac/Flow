namespace Flow.Shared.Contracts.Boards;

/// <summary>
/// Тип задачи проекта. Level — уровень иерархии (1 — эпик … 4 — подзадача), выводится из Kind.
/// Архивные типы тоже приходят: их носят старые задачи, но в выборе при создании их быть не должно.
/// Массив типов в BoardResponse отсортирован сервером.
/// </summary>
public sealed record TaskTypeResponse(Guid Id, string Name, TaskTypeKind Kind, int Level, bool IsDefault, bool IsArchived);
