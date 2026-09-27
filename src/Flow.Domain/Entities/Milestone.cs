namespace Flow.Domain.Entities;

/// <summary>Open → Closed и обратно: закрытую веху можно открыть снова (релиз отозвали).</summary>
public enum MilestoneState
{
    Open = 0,
    Closed = 1
}

/// <summary>
/// Веха проекта (docs/TZ_task_views.md §6) — результат или релиз с целевой датой. Задачи ссылаются на неё через
/// TaskItem.MilestoneId; веха и спринт независимы. Закрыть веху с открытыми задачами можно — задачи остаются в ней,
/// но новые в закрытую веху не добавляются.
/// </summary>
public sealed class Milestone
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 2000;

    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateOnly? TargetDate { get; private set; }

    public MilestoneState State { get; private set; }

    public DateTime? ClosedAt { get; private set; }

    /// <summary>Порядок в списке вех проекта; растёт при создании.</summary>
    public int SortOrder { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private Milestone()
    {
        // EF Core
    }

    private Milestone(Guid boardId, string name, string? description, DateOnly? targetDate, int sortOrder)
    {
        Id = Guid.NewGuid();
        BoardId = boardId;
        Name = ValidateName(name);
        Description = ValidateDescription(description);
        TargetDate = targetDate;
        SortOrder = sortOrder;
        State = MilestoneState.Open;
        CreatedAt = DateTime.UtcNow;
    }

    public static Milestone Create(Guid boardId, string name, string? description, DateOnly? targetDate, int sortOrder) =>
        new(boardId, name, description, targetDate, sortOrder);

    public bool IsClosed => State == MilestoneState.Closed;

    public void Rename(string name) => Name = ValidateName(name);

    public void SetDescription(string? description) => Description = ValidateDescription(description);

    /// <summary>Прошедшая дата допустима: веха могла сорваться, и это видно по прогрессу.</summary>
    public void SetTargetDate(DateOnly? targetDate) => TargetDate = targetDate;

    public void Close(DateTime utcNow)
    {
        if (IsClosed)
            throw new InvalidOperationException("The milestone is already closed.");

        State = MilestoneState.Closed;
        ClosedAt = utcNow;
    }

    public void Reopen()
    {
        if (!IsClosed)
            throw new InvalidOperationException("The milestone is already open.");

        State = MilestoneState.Open;
        ClosedAt = null;
    }

    private static string ValidateName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("Milestone name is required.", nameof(name));
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Milestone name must be at most {NameMaxLength} characters.", nameof(name));
        return trimmed;
    }

    private static string? ValidateDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > DescriptionMaxLength)
            throw new ArgumentException($"Milestone description must be at most {DescriptionMaxLength} characters.", nameof(description));
        return trimmed;
    }
}
