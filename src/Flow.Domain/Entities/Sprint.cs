namespace Flow.Domain.Entities;

/// <summary>Planned → Active → Completed; назад дороги нет (docs/TZ_task_views.md §2).</summary>
public enum SprintState
{
    Planned = 0,
    Active = 1,
    Completed = 2
}

/// <summary>
/// Committed — задача была в спринте в момент старта (снимок для отчёта: оценку потом могут поменять),
/// CarriedOver — была в спринте незакрытой в момент завершения и уехала дальше («не сделано»).
/// </summary>
public enum SprintCommitmentKind
{
    Committed = 0,
    CarriedOver = 1
}

/// <summary>Строка снимка спринта: задача и её story points на момент записи.</summary>
public sealed class SprintCommitment
{
    public Guid SprintId { get; private set; }

    public Guid TaskId { get; private set; }

    public SprintCommitmentKind Kind { get; private set; }

    public decimal? StoryPoints { get; private set; }

    private SprintCommitment()
    {
        // EF Core
    }

    internal SprintCommitment(Guid sprintId, Guid taskId, SprintCommitmentKind kind, decimal? storyPoints)
    {
        SprintId = sprintId;
        TaskId = taskId;
        Kind = kind;
        StoryPoints = storyPoints;
    }
}

/// <summary>
/// Спринт проекта (docs/TZ_task_views.md §2) — свой агрегат: задачи ссылаются на него через TaskItem.SprintId,
/// а сам спринт держит только снимки. «Не больше одного активного в проекте» домену не видно (соседние спринты
/// не загружены) — это проверяет Application, а страхует частичный unique-индекс в БД.
/// </summary>
public sealed class Sprint
{
    public const int NameMaxLength = 60;
    public const int GoalMaxLength = 500;

    private readonly List<SprintCommitment> _commitments = [];

    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Goal { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public SprintState State { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    /// <summary>Порядок запланированных спринтов в бэклоге; растёт при создании.</summary>
    public int SortOrder { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public IReadOnlyCollection<SprintCommitment> Commitments => _commitments;

    private Sprint()
    {
        // EF Core
    }

    private Sprint(Guid boardId, string name, string? goal, int sortOrder)
    {
        Id = Guid.NewGuid();
        BoardId = boardId;
        Name = ValidateName(name);
        Goal = ValidateGoal(goal);
        SortOrder = sortOrder;
        State = SprintState.Planned;
        CreatedAt = DateTime.UtcNow;
    }

    public static Sprint Create(Guid boardId, string name, string? goal, int sortOrder, DateOnly? start = null, DateOnly? end = null)
    {
        var sprint = new Sprint(boardId, name, goal, sortOrder);
        sprint.SetDates(start, end);
        return sprint;
    }

    public bool IsCompleted => State == SprintState.Completed;

    /// <summary>Название меняется всегда — единственное, что можно у завершённого спринта.</summary>
    public void Rename(string name) => Name = ValidateName(name);

    public void SetGoal(string? goal)
    {
        EnsureNotCompleted();
        Goal = ValidateGoal(goal);
    }

    /// <summary>Даты — запланированного и активного (продлить спринт можно); конец не раньше начала.</summary>
    public void SetDates(DateOnly? start, DateOnly? end)
    {
        EnsureNotCompleted();
        if (start is { } s && end is { } e && e <= s)
            throw new ArgumentException("Sprint end date must be after its start date.", nameof(end));
        if (State == SprintState.Active && (start is null || end is null))
            throw new InvalidOperationException("An active sprint must keep both dates.");

        StartDate = start;
        EndDate = end;
    }

    /// <summary>
    /// Старт: только из Planned, обе даты, конец позже начала. Снимок обязательств — задачи спринта с их
    /// story points прямо сейчас: по нему отчёт считает «взято на старте».
    /// </summary>
    public void Start(DateOnly start, DateOnly end, IEnumerable<TaskItem> tasks, DateTime utcNow)
    {
        if (State != SprintState.Planned)
            throw new InvalidOperationException("Only a planned sprint can be started.");
        if (end <= start)
            throw new ArgumentException("Sprint end date must be after its start date.", nameof(end));

        StartDate = start;
        EndDate = end;
        State = SprintState.Active;
        StartedAt = utcNow;
        foreach (var task in tasks.Where(t => t.SprintId == Id))
            _commitments.Add(new SprintCommitment(Id, task.Id, SprintCommitmentKind.Committed, task.StoryPoints));
    }

    /// <summary>
    /// Завершение: только активного. Незакрытые задачи уезжают в <paramref name="moveOpenTo"/> (другой незавершённый
    /// спринт того же проекта) или в бэклог (null) и попадают в снимок как «не сделано»; закрытые остаются здесь
    /// навсегда — на них держится история и отчёт. Возвращает переехавшие задачи — хендлеру для журнала.
    /// </summary>
    public IReadOnlyList<TaskItem> Complete(IEnumerable<TaskItem> openTasks, Sprint? moveOpenTo, DateTime utcNow)
    {
        if (State != SprintState.Active)
            throw new InvalidOperationException("Only an active sprint can be completed.");
        if (moveOpenTo is not null && (moveOpenTo.BoardId != BoardId || moveOpenTo.IsCompleted || moveOpenTo.Id == Id))
            throw new InvalidOperationException("Open tasks can move only to another unfinished sprint of the same project.");

        var moved = openTasks.Where(t => t.SprintId == Id).ToList();
        foreach (var task in moved)
        {
            _commitments.Add(new SprintCommitment(Id, task.Id, SprintCommitmentKind.CarriedOver, task.StoryPoints));
            task.SetSprint(moveOpenTo);
        }

        State = SprintState.Completed;
        CompletedAt = utcNow;
        return moved;
    }

    private void EnsureNotCompleted()
    {
        if (IsCompleted)
            throw new InvalidOperationException("A completed sprint can only be renamed.");
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sprint name must not be empty.", nameof(name));

        var trimmed = name.Trim();
        return trimmed.Length > NameMaxLength
            ? throw new ArgumentException($"Sprint name must be at most {NameMaxLength} characters.", nameof(name))
            : trimmed;
    }

    private static string? ValidateGoal(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return null;

        var trimmed = goal.Trim();
        return trimmed.Length > GoalMaxLength
            ? throw new ArgumentException($"Sprint goal must be at most {GoalMaxLength} characters.", nameof(goal))
            : trimmed;
    }
}
