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
    /// Родитель в иерархии (docs/TZ_task_model.md §3); null — задача верхнего уровня. Родитель всегда в том же
    /// проекте и строго выше по уровню типа, поэтому циклов и глубины больше четырёх не бывает по построению.
    /// </summary>
    public Guid? ParentId { get; private set; }

    /// <summary>
    /// Ручной порядок в проекте — ключ <see cref="FractionalIndex"/> (§7), сравнивается только ординально.
    /// Порядок в колонке канбана и в бэклоге — этот же ранг, отфильтрованный по статусу или спринту.
    /// </summary>
    public string Rank { get; private set; } = FractionalIndex.First;

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

    /// <summary>Ставит IUnitOfWork при сохранении изменённой задачи; вызывать из хендлеров не нужно.</summary>
    public void Touch(DateTime utcNow) => UpdatedAt = utcNow;

    private static string ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Task title must not be empty.", nameof(title));

        return title.Trim();
    }
}
