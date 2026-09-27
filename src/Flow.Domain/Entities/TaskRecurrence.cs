namespace Flow.Domain.Entities;

/// <summary>
/// Повторение задачи (docs/TZ_task_model.md §9): образец — сама задача, копии — обычные задачи со связью Clones.
/// Одно правило на задачу. Генератор создаёт копии на даты правила до «сегодня + LeadDays», после
/// <see cref="GeneratedUntil"/>; идемпотентность держит PK <see cref="TaskRecurrenceOccurrence"/> (правило, дата).
/// Автор деактивирован — правило на паузе (<see cref="Pause"/>): тихих копий от имени ушедшего человека нет.
/// </summary>
public sealed class TaskRecurrence
{
    public const int MaxLeadDays = 60;
    public const int MaxDueOffsetDays = 365;
    public const int ErrorMaxLength = 500;

    /// <summary>После простоя копии создаются только на даты не старше недели — без пачки задач «на вчера».</summary>
    public const int CatchUpDays = 7;

    public Guid Id { get; private set; }

    public Guid TemplateTaskId { get; private set; }

    public Guid BoardId { get; private set; }

    public RecurrenceRule Rule { get; private set; } = null!;

    public DateOnly StartsOn { get; private set; }

    public DateOnly? EndsOn { get; private set; }

    /// <summary>За сколько дней до даты создавать копию, 0…60.</summary>
    public int LeadDays { get; private set; }

    /// <summary>Срок копии — дата повторения плюс столько дней; null — без срока.</summary>
    public int? DueOffsetDays { get; private set; }

    public bool CopyAssignee { get; private set; }

    public bool CopyChecklist { get; private set; }

    public bool IsActive { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Последняя дата, на которую копия уже создана (или пропущена); null — ещё ни одной.</summary>
    public DateOnly? GeneratedUntil { get; private set; }

    /// <summary>Почему правило остановилось или последняя генерация сорвалась.</summary>
    public string? LastError { get; private set; }

    private TaskRecurrence()
    {
        // EF Core
    }

    public static TaskRecurrence Create(TaskItem template, Guid createdById, RecurrenceRule rule, DateOnly startsOn, DateOnly? endsOn,
        int leadDays, int? dueOffsetDays, bool copyAssignee, bool copyChecklist)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (createdById == Guid.Empty)
            throw new ArgumentException("Author id must not be empty.", nameof(createdById));

        var recurrence = new TaskRecurrence
        {
            Id = Guid.NewGuid(),
            TemplateTaskId = template.Id,
            BoardId = template.BoardId,
            CreatedById = createdById,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        recurrence.Update(rule, startsOn, endsOn, leadDays, dueOffsetDays, copyAssignee, copyChecklist);
        return recurrence;
    }

    /// <summary>
    /// Правка правила. Уже созданные копии остаются; даты до <see cref="GeneratedUntil"/> не пересоздаются, а
    /// дата, на которую копия уже есть, отсекается ключом вхождения.
    /// </summary>
    public void Update(RecurrenceRule rule, DateOnly startsOn, DateOnly? endsOn, int leadDays, int? dueOffsetDays, bool copyAssignee, bool copyChecklist)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (endsOn is { } end && end < startsOn)
            throw new ArgumentException("Дата окончания раньше даты начала.", nameof(endsOn));
        if (leadDays is < 0 or > MaxLeadDays)
            throw new ArgumentException($"Создавать заранее — от 0 до {MaxLeadDays} дней.", nameof(leadDays));
        if (dueOffsetDays is < 0 or > MaxDueOffsetDays)
            throw new ArgumentException($"Срок — от 0 до {MaxDueOffsetDays} дней после даты.", nameof(dueOffsetDays));

        Rule = rule;
        StartsOn = startsOn;
        EndsOn = endsOn;
        LeadDays = leadDays;
        DueOffsetDays = dueOffsetDays;
        CopyAssignee = copyAssignee;
        CopyChecklist = copyChecklist;
    }

    /// <summary>Возобновить (из паузы или после ошибки); автора возобновления правило берёт себе.</summary>
    public void Resume(Guid actorId)
    {
        if (actorId == Guid.Empty)
            throw new ArgumentException("Actor id must not be empty.", nameof(actorId));

        IsActive = true;
        CreatedById = actorId;
        LastError = null;
    }

    public void Pause(string? reason = null)
    {
        IsActive = false;
        LastError = reason is null ? null : reason.Length <= ErrorMaxLength ? reason : reason[..ErrorMaxLength];
    }

    public void RecordError(string error) =>
        LastError = error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];

    /// <summary>
    /// Даты, на которые пора создать копии к дню <paramref name="today"/>: после <see cref="GeneratedUntil"/>,
    /// до сегодня + LeadDays, не позже конца и не старше <see cref="CatchUpDays"/> дней.
    /// </summary>
    public IReadOnlyList<DateOnly> DueDates(DateOnly today)
    {
        if (!IsActive)
            return [];

        var from = new[] { StartsOn, today.AddDays(-CatchUpDays), GeneratedUntil?.AddDays(1) ?? DateOnly.MinValue }.Max();
        var to = today.AddDays(LeadDays);
        if (EndsOn is { } end && end < to)
            to = end;

        return Rule.Occurrences(StartsOn, from, to).ToList();
    }

    /// <summary>Ближайшие даты — для превью в интерфейсе.</summary>
    public IReadOnlyList<DateOnly> NextDates(DateOnly today, int count)
    {
        var from = new[] { StartsOn, today, GeneratedUntil?.AddDays(1) ?? DateOnly.MinValue }.Max();
        var to = EndsOn ?? from.AddYears(10);
        return Rule.Occurrences(StartsOn, from, to).Take(count).ToList();
    }

    /// <summary>Отметить, что до этой даты включительно генерация дошла; пропущенные старые даты тоже считаются пройденными.</summary>
    public void MarkGenerated(DateOnly until)
    {
        if (GeneratedUntil is null || until > GeneratedUntil)
            GeneratedUntil = until;
        LastError = null;
    }
}

/// <summary>Созданная копия (правило, дата) — PK пары держит идемпотентность генерации. Удалили копию — дата остаётся занятой.</summary>
public sealed class TaskRecurrenceOccurrence
{
    public Guid RecurrenceId { get; private set; }

    public DateOnly OccursOn { get; private set; }

    public Guid? TaskId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private TaskRecurrenceOccurrence()
    {
        // EF Core
    }

    public static TaskRecurrenceOccurrence Create(Guid recurrenceId, DateOnly occursOn, Guid taskId) =>
        new() { RecurrenceId = recurrenceId, OccursOn = occursOn, TaskId = taskId, CreatedAt = DateTime.UtcNow };
}
