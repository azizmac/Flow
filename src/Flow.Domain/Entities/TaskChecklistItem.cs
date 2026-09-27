namespace Flow.Domain.Entities;

/// <summary>
/// Пункт чек-листа задачи (docs/TZ_task_model.md §8): строка без статуса, исполнителя и кода — не подзадача.
/// Owned-коллекция <see cref="TaskItem"/>, меняется только через её методы (лимит, порядок).
/// </summary>
public sealed class TaskChecklistItem
{
    public const int TextMaxLength = 500;

    public Guid Id { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public bool IsDone { get; private set; }

    public int SortOrder { get; private set; }

    public DateTime? DoneAt { get; private set; }

    public Guid? DoneById { get; private set; }

    private TaskChecklistItem()
    {
        // EF Core
    }

    internal TaskChecklistItem(string text, int sortOrder)
    {
        Id = Guid.NewGuid();
        Text = ValidateText(text);
        SortOrder = sortOrder;
    }

    internal void Edit(string text) => Text = ValidateText(text);

    internal void SetDone(bool isDone, Guid actorId)
    {
        IsDone = isDone;
        DoneAt = isDone ? DateTime.UtcNow : null;
        DoneById = isDone ? actorId : null;
    }

    internal void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    internal static string ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Checklist item text must not be empty.", nameof(text));

        var trimmed = text.Trim();
        if (trimmed.Length > TextMaxLength)
            throw new ArgumentException($"Checklist item text must be at most {TextMaxLength} characters.", nameof(text));

        return trimmed;
    }
}
