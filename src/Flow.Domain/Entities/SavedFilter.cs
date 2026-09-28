namespace Flow.Domain.Entities;

public enum SavedFilterVisibility
{
    Private = 0,
    Shared = 1
}

/// <summary>
/// Сохранённый фильтр задач (docs/TZ_task_views.md §7): имя и строка FQL. Общий видят все, но результат каждому
/// считается его правами — фильтр хранит запрос, а не задачи, и приватный проект не раскрывает. Правильность
/// FQL проверяет Application (биндинг зависит от того, кто спрашивает); здесь — только длины.
/// </summary>
public sealed class SavedFilter
{
    public const int NameMaxLength = 80;
    public const int QueryMaxLength = 4000;

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Query { get; private set; } = string.Empty;

    /// <summary>Представление, в котором открывается фильтр; пока есть только список (канбан — этап 2B).</summary>
    public string View { get; private set; } = "list";

    public SavedFilterVisibility Visibility { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    private SavedFilter()
    {
        // EF Core
    }

    public static SavedFilter Create(Guid ownerId, string name, string query, SavedFilterVisibility visibility)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException("Owner id must not be empty.", nameof(ownerId));

        var filter = new SavedFilter { Id = Guid.NewGuid(), OwnerId = ownerId, CreatedAt = DateTime.UtcNow };
        filter.Update(name, query, visibility);
        return filter;
    }

    /// <summary>PATCH-семантика: null — не трогать.</summary>
    public void Update(string? name = null, string? query = null, SavedFilterVisibility? visibility = null)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Filter name must not be empty.", nameof(name));
            var trimmed = name.Trim();
            if (trimmed.Length > NameMaxLength)
                throw new ArgumentException($"Filter name must be at most {NameMaxLength} characters.", nameof(name));
            Name = trimmed;
        }

        if (query is not null)
        {
            var trimmed = query.Trim();
            if (trimmed.Length > QueryMaxLength)
                throw new ArgumentException($"Filter query must be at most {QueryMaxLength} characters.", nameof(query));
            Query = trimmed;
        }

        if (visibility is { } v)
        {
            if (!Enum.IsDefined(v))
                throw new ArgumentException($"Unknown visibility {v}.", nameof(visibility));
            Visibility = v;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsVisibleTo(Guid userId) => OwnerId == userId || Visibility == SavedFilterVisibility.Shared;
}

/// <summary>Фильтр в избранном пользователя — показывается в сайдбаре.</summary>
public sealed class SavedFilterStar
{
    public Guid FilterId { get; private set; }

    public Guid UserId { get; private set; }

    private SavedFilterStar()
    {
        // EF Core
    }

    public SavedFilterStar(Guid filterId, Guid userId)
    {
        FilterId = filterId;
        UserId = userId;
    }
}
