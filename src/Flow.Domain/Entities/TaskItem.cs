using System.Text.Json;
using System.Text.Json.Nodes;
using Flow.Domain.Ranking;

namespace Flow.Domain.Entities;

/// <summary>
/// Задача на доске. Создаётся только через <see cref="Board.CreateTask"/>, никогда напрямую,
/// чтобы код задачи (<see cref="TaskCode"/>) и статус по умолчанию всегда были согласованы с доской.
/// </summary>
public sealed class TaskItem
{
    /// <summary>Потолок story points: шкалу (Фибоначчи и т.п.) навязывает интерфейс, сервер проверяет только диапазон.</summary>
    public const decimal MaxStoryPoints = 999.9m;

    /// <summary>Потолок оценки — 999 рабочих дней по 8 часов, в минутах.</summary>
    public const int MaxEstimateMinutes = 999 * 8 * 60;

    /// <summary>Потолок пунктов чек-листа: длиннее — это уже подзадачи (docs/TZ_task_model.md §8).</summary>
    public const int MaxChecklistItems = 100;

    private readonly List<TaskChecklistItem> _checklist = [];

    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    public TaskCode Code { get; private set; } = null!;

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid StatusId { get; private set; }

    /// <summary>Исполнитель (User.Id); null — не назначен. Активность пользователя проверяет вызывающая сторона, как и в ChangeStatus.</summary>
    public Guid? AssigneeId { get; private set; }

    /// <summary>Кто создал (User.Id). null — задачи, созданные до появления ролей. Нужен для «своей задачи» у Member.</summary>
    public Guid? CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Срок (дата без времени); null — без срока. Прошедшие даты допустимы: старые задачи переносят задним числом.</summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>Дата начала; вместе с <see cref="DueDate"/> задаёт отрезок работы (роадмап). Не позже срока.</summary>
    public DateOnly? StartDate { get; private set; }

    /// <summary>Тип задачи (<see cref="TaskType"/> того же проекта); задаёт вид и уровень иерархии.</summary>
    public Guid TypeId { get; private set; }

    public TaskPriority Priority { get; private set; }

    /// <summary>Оценка в story points, 0…999.9 с шагом 0.1; null — не оценена.</summary>
    public decimal? StoryPoints { get; private set; }

    /// <summary>Оценка времени в минутах; null — не оценена. Учёта фактического времени нет намеренно.</summary>
    public int? EstimateMinutes { get; private set; }

    /// <summary>
    /// Момент последнего изменения задачи. Ставит не каждый метод, а IUnitOfWork при сохранении изменённой
    /// сущности: иначе новое поле, забывшее «тронуть» дату, тихо ломало бы сортировку «по изменению».
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Когда задача последний раз сменила статус — по нему финальная колонка канбана отбирает «закрытые за N дней».
    /// Ставит IUnitOfWork при сохранении, как и UpdatedAt: любой путь смены статуса (правка, перетаскивание,
    /// перенос при удалении статуса) не сможет её забыть.
    /// </summary>
    public DateTime StatusChangedAt { get; private set; }

    /// <summary>Спринт задачи (docs/TZ_task_views.md §2); null — бэклог. FK SetNull: удаление спринта возвращает в бэклог.</summary>
    public Guid? SprintId { get; private set; }

    /// <summary>Веха задачи (docs/TZ_task_views.md §6); независима от спринта. FK SetNull: удаление вехи её снимает.</summary>
    public Guid? MilestoneId { get; private set; }

    /// <summary>
    /// Значения пользовательских полей (docs/TZ_task_model.md §4): JSON-объект «Id поля → значение», в БД — jsonb.
    /// Ключ — Id, а не Key поля: переименование ключа задачи не трогает. Менять — только <see cref="SetCustomField"/>.
    /// </summary>
    public string CustomFieldsJson { get; private set; } = "{}";

    /// <summary>
    /// Родитель в иерархии (docs/TZ_task_model.md §3); null — задача верхнего уровня. Родитель всегда в том же
    /// проекте и строго выше по уровню типа, поэтому циклов и глубины больше четырёх не бывает по построению.
    /// </summary>
    public Guid? ParentId { get; private set; }

    /// <summary>
    /// Ручной порядок в проекте — ключ <see cref="FractionalIndex"/> (§7), сравнивается только ординально.
    /// Порядок в колонке канбана и в бэклоге — этот же ранг, отфильтрованный по статусу или спринту.
    /// </summary>
    public string Rank { get; private set; } = FractionalIndex.First;

    /// <summary>Чек-лист (§8) — по SortOrder. Owned-коллекция: грузится вместе с задачей.</summary>
    public IReadOnlyList<TaskChecklistItem> Checklist => _checklist.OrderBy(i => i.SortOrder).ToList();

    public int ChecklistDone => _checklist.Count(i => i.IsDone);

    public int ChecklistTotal => _checklist.Count;

    private TaskItem()
    {
        // EF Core
    }

    internal TaskItem(Guid boardId, TaskCode code, string title, string? description, Guid statusId, Guid? createdById, Guid typeId, string rank)
    {
        FractionalIndex.Validate(rank);
        Rank = rank;
        Id = Guid.NewGuid();
        BoardId = boardId;
        Code = code;
        Title = ValidateTitle(title);
        Description = description;
        StatusId = statusId;
        CreatedById = createdById;
        TypeId = typeId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
        StatusChangedAt = CreatedAt;
    }

    public void Rename(string title) => Title = ValidateTitle(title);

    public void UpdateDescription(string? description) => Description = description;

    /// <summary>
    /// Меняет статус задачи. Сама не проверяет, что <paramref name="statusId"/> принадлежит той же доске —
    /// эту проверку делает вызывающая сторона, у которой есть доступ к списку статусов доски
    /// (см. Board.Statuses / соответствующий эндпоинт).
    /// </summary>
    public void ChangeStatus(Guid statusId) => StatusId = statusId;

    /// <summary>
    /// Назначает исполнителя. Не проверяет, что пользователь существует и активен — у сущности нет доступа
    /// к пользователям; проверку делает Application (TaskAssignCommandHandler).
    /// </summary>
    public void Assign(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("Assignee id must not be empty.", nameof(userId));

        AssigneeId = userId;
    }

    public void Unassign() => AssigneeId = null;

    /// <summary>null — снять срок. Срок раньше даты начала — ошибка (см. <see cref="SetSchedule"/>).</summary>
    public void SetDueDate(DateOnly? dueDate) => SetSchedule(StartDate, dueDate);

    /// <summary>
    /// Задаёт обе даты сразу: по отдельности перенос отрезка «начало–срок» вперёд упирался бы в проверку
    /// на промежуточном шаге. null — снять. Прошедшие даты допустимы, как и у срока.
    /// </summary>
    public void SetSchedule(DateOnly? startDate, DateOnly? dueDate)
    {
        if (startDate is not null && dueDate is not null && startDate > dueDate)
            throw new ArgumentException("Start date must not be later than the due date.", nameof(startDate));

        StartDate = startDate;
        DueDate = dueDate;
    }

    public void SetPriority(TaskPriority priority)
    {
        if (!Enum.IsDefined(priority))
            throw new ArgumentException($"Unknown priority {priority}.", nameof(priority));

        Priority = priority;
    }

    /// <summary>
    /// Меняет тип. Тип должен быть из того же проекта и не архивным, а его уровень — строго между уровнем
    /// родителя и уровнем самого высокого ребёнка (§3): эпик с историями нельзя сделать подзадачей.
    /// Уровни родителя и детей приносит хендлер — у задачи нет доступа к соседям.
    /// </summary>
    public void ChangeType(TaskType type, int? parentLevel = null, int? minChildLevel = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.BoardId != BoardId)
            throw new InvalidOperationException($"Task type {type.Id} does not belong to board {BoardId}.");
        if (type.IsArchived && type.Id != TypeId)
            throw new InvalidOperationException($"Task type {type.Id} is archived.");
        if (parentLevel is { } parent && type.Level <= parent)
            throw new InvalidOperationException($"Task type {type.Name} is not below the parent task's type.");
        if (minChildLevel is { } child && type.Level >= child)
            throw new InvalidOperationException($"Task type {type.Name} is not above the subtasks' types.");

        TypeId = type.Id;
    }

    /// <summary>
    /// Ставит или снимает родителя (null). Родитель — из того же проекта, не сама задача, и его тип строго выше
    /// по уровню. Циклы при таком правиле невозможны: уровень растёт вниз, проверять предков не нужно.
    /// Типы приносит хендлер (они у проекта); их соответствие задачам проверяется здесь.
    /// </summary>
    public void SetParent(TaskItem? parent, TaskType ownType, TaskType? parentType)
    {
        ArgumentNullException.ThrowIfNull(ownType);
        if (ownType.Id != TypeId)
            throw new ArgumentException($"Type {ownType.Id} is not the task's type.", nameof(ownType));

        if (parent is null)
        {
            ParentId = null;
            return;
        }

        ArgumentNullException.ThrowIfNull(parentType);
        if (parentType.Id != parent.TypeId)
            throw new ArgumentException($"Type {parentType.Id} is not the parent's type.", nameof(parentType));
        if (parent.Id == Id)
            throw new InvalidOperationException("A task cannot be its own parent.");
        if (parent.BoardId != BoardId)
            throw new InvalidOperationException("The parent task must belong to the same project.");
        if (parentType.Level >= ownType.Level)
            throw new InvalidOperationException($"A {parentType.Name} cannot be the parent of a {ownType.Name}: the parent must be higher in the hierarchy.");

        ParentId = parent.Id;
    }

    /// <summary>Ранг вычисляет сервер по соседям (<see cref="FractionalIndex.Between"/>); здесь — только проверка формата.</summary>
    public void SetRank(string rank)
    {
        FractionalIndex.Validate(rank);
        Rank = rank;
    }

    /// <summary>null — снять оценку.</summary>
    public void SetStoryPoints(decimal? storyPoints)
    {
        if (storyPoints is { } points)
        {
            if (points < 0 || points > MaxStoryPoints)
                throw new ArgumentException($"Story points must be between 0 and {MaxStoryPoints}.", nameof(storyPoints));
            if (decimal.Round(points, 1) != points)
                throw new ArgumentException("Story points allow at most one decimal place.", nameof(storyPoints));
        }

        StoryPoints = storyPoints;
    }

    /// <summary>null — снять оценку.</summary>
    public void SetEstimate(int? estimateMinutes)
    {
        if (estimateMinutes is < 0 or > MaxEstimateMinutes)
            throw new ArgumentException($"Estimate must be between 0 and {MaxEstimateMinutes} minutes.", nameof(estimateMinutes));

        EstimateMinutes = estimateMinutes;
    }

    public TaskChecklistItem AddChecklistItem(string text)
    {
        if (_checklist.Count >= MaxChecklistItems)
            throw new InvalidOperationException($"A checklist holds at most {MaxChecklistItems} items; split the task instead.");

        var item = new TaskChecklistItem(text, _checklist.Count == 0 ? 0 : _checklist.Max(i => i.SortOrder) + 1);
        _checklist.Add(item);
        return item;
    }

    /// <summary>Возвращает, изменился ли текст: тот же текст — no-op.</summary>
    public bool EditChecklistItem(Guid itemId, string text)
    {
        var item = GetChecklistItem(itemId);
        var normalized = TaskChecklistItem.ValidateText(text);
        if (normalized == item.Text)
            return false;

        item.Edit(normalized);
        return true;
    }

    /// <summary>Отметить или снять отметку; кто и когда отметил — для подсказки у пункта. То же состояние — no-op.</summary>
    public bool SetChecklistItemDone(Guid itemId, bool isDone, Guid actorId)
    {
        var item = GetChecklistItem(itemId);
        if (item.IsDone == isDone)
            return false;

        item.SetDone(isDone, actorId);
        return true;
    }

    public void RemoveChecklistItem(Guid itemId) => _checklist.Remove(GetChecklistItem(itemId));

    /// <summary>Новый порядок — полная перестановка Id пунктов, иначе ArgumentException.</summary>
    public void ReorderChecklist(IReadOnlyList<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count != _checklist.Count || itemIds.Distinct().Count() != itemIds.Count
            || itemIds.Any(id => _checklist.All(i => i.Id != id)))
            throw new ArgumentException("Checklist order must list every item exactly once.", nameof(itemIds));

        for (var i = 0; i < itemIds.Count; i++)
            _checklist.Single(x => x.Id == itemIds[i]).SetSortOrder(i);
    }

    private TaskChecklistItem GetChecklistItem(Guid itemId) =>
        _checklist.SingleOrDefault(i => i.Id == itemId)
        ?? throw new InvalidOperationException($"Checklist item {itemId} does not belong to task {Id}.");

    /// <summary>Ставит IUnitOfWork при сохранении изменённой задачи; вызывать из хендлеров не нужно.</summary>
    public void Touch(DateTime utcNow) => UpdatedAt = utcNow;

    /// <summary>
    /// Перенос в спринт того же проекта или в бэклог (null). В завершённый спринт задачу не переносят:
    /// его состав — история. Спринт приносит хендлер, как типы для SetParent.
    /// </summary>
    public void SetSprint(Sprint? sprint)
    {
        if (sprint is not null && sprint.BoardId != BoardId)
            throw new InvalidOperationException("A task can be planned only into a sprint of its own project.");
        if (sprint is { IsCompleted: true } && sprint.Id != SprintId)
            throw new InvalidOperationException("A completed sprint cannot take new tasks.");

        SprintId = sprint?.Id;
    }

    /// <summary>
    /// Веха своего проекта (или общая с ним, этап 2H) или null. В закрытую веху новые задачи не добавляют: её состав — итог релиза;
    /// задачи, уже бывшие в ней к закрытию, остаются.
    /// </summary>
    public void SetMilestone(Milestone? milestone)
    {
        if (milestone is not null && !milestone.IsAvailableIn(BoardId))
            throw new InvalidOperationException("A task can be put only into a milestone of its own project.");
        if (milestone is { IsClosed: true } && milestone.Id != MilestoneId)
            throw new InvalidOperationException("A closed milestone cannot take new tasks.");

        MilestoneId = milestone?.Id;
    }

    /// <summary>Значение поля или null — не заполнено. Копия: исходный документ живёт только внутри вызова.</summary>
    public JsonElement? GetCustomField(Guid fieldId)
    {
        using var document = JsonDocument.Parse(CustomFieldsJson);
        return document.RootElement.TryGetProperty(fieldId.ToString(), out var value) ? value.Clone() : null;
    }

    /// <summary>Все значения задачи, в том числе полей, которые с тех пор архивированы.</summary>
    public IReadOnlyDictionary<Guid, JsonElement> CustomFieldValues()
    {
        using var document = JsonDocument.Parse(CustomFieldsJson);
        return document.RootElement.EnumerateObject()
            .Where(p => Guid.TryParse(p.Name, out _))
            .ToDictionary(p => Guid.Parse(p.Name), p => p.Value.Clone());
    }

    /// <summary>
    /// Ставит или очищает (null) значение поля своего проекта: поле должно быть у типа задачи и не в архиве,
    /// значение проходит <see cref="CustomFieldValidator"/>, обязательное не очищается. Возвращает прежнее и новое
    /// значение JSON-строками (null — пусто) или null, если ничего не поменялось.
    /// </summary>
    public (string? Old, string? New)? SetCustomField(CustomFieldDefinition field, JsonElement? value)
    {
        if (field.BoardId != BoardId)
            throw new InvalidOperationException("The custom field belongs to another project.");
        if (!field.AppliesTo(TypeId))
            throw new InvalidOperationException($"Поля «{field.Name}» у задач этого типа нет.");

        var normalized = value is { } v ? CustomFieldValidator.Validate(field, v) : null;
        if (normalized is null && field.IsRequired)
            throw new InvalidOperationException($"Поле «{field.Name}» обязательное.");

        var values = JsonNode.Parse(CustomFieldsJson)!.AsObject();
        var key = field.Id.ToString();
        var old = values.TryGetPropertyValue(key, out var existing) ? existing?.ToJsonString(CustomFieldValidator.JsonOptions) : null;
        var next = normalized?.ToJsonString(CustomFieldValidator.JsonOptions);
        if (old == next)
            return null;

        if (normalized is null)
            values.Remove(key);
        else
            values[key] = normalized;

        CustomFieldsJson = values.ToJsonString(CustomFieldValidator.JsonOptions);
        return (old, next);
    }

    /// <summary>
    /// Копия значений пользовательских полей задачи того же проекта — для разделения (§6): новые части
    /// наследуют поля образца. Значения уже прошли валидатор у образца, повторно их не проверяем.
    /// </summary>
    public void CopyCustomFieldsFrom(TaskItem source)
    {
        if (source.BoardId != BoardId)
            throw new InvalidOperationException("Custom field values can be copied only within one project.");

        CustomFieldsJson = source.CustomFieldsJson;
    }

    /// <summary>
    /// Переезд в другой проект (§6) — только через <see cref="Board.ReceiveTask"/>: код, статус, тип, ранг и значения
    /// полей уже пересчитаны под целевой проект. Спринт и веха принадлежат проекту и сбрасываются; родитель остаётся,
    /// только если он переезжает вместе с задачей (поддерево).
    /// </summary>
    internal void Relocate(Guid boardId, TaskCode code, Guid statusId, Guid typeId, string rank, string customFieldsJson, bool keepParent)
    {
        FractionalIndex.Validate(rank);
        BoardId = boardId;
        Code = code;
        StatusId = statusId;
        TypeId = typeId;
        Rank = rank;
        CustomFieldsJson = customFieldsJson;
        SprintId = null;
        MilestoneId = null;
        if (!keepParent)
            ParentId = null;
    }

    /// <summary>Ставит IUnitOfWork, когда при сохранении изменился StatusId.</summary>
    public void MarkStatusChanged(DateTime utcNow) => StatusChangedAt = utcNow;

    private static string ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Task title must not be empty.", nameof(title));

        return title.Trim();
    }
}
