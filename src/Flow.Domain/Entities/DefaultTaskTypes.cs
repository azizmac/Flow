namespace Flow.Domain.Entities;

/// <summary>Набор типов, которым <see cref="Board.Create"/> засеивает новый проект (как <see cref="DefaultStatuses"/>).</summary>
public static class DefaultTaskTypes
{
    public static readonly IReadOnlyList<TaskTypeDefinition> All =
    [
        new(TaskTypeKind.Epic, "Эпик", IsDefault: false),
        new(TaskTypeKind.Story, "История", IsDefault: false),
        new(TaskTypeKind.Task, "Задача", IsDefault: true),
        new(TaskTypeKind.Bug, "Ошибка", IsDefault: false),
        new(TaskTypeKind.Subtask, "Подзадача", IsDefault: false)
    ];

    public sealed record TaskTypeDefinition(TaskTypeKind Kind, string Name, bool IsDefault);
}
