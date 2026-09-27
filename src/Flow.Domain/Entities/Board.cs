using System.Text.RegularExpressions;

namespace Flow.Domain.Entities;

/// <summary>
/// Доска (aggregate root). Владеет своими статусами и задачами, генерирует человекочитаемый
/// код задачи (<see cref="TaskCode"/>) вида "FLW-42" на основе <see cref="Key"/> и счётчика <see cref="NextTaskNumber"/>.
/// </summary>
public sealed partial class Board
{
    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}$")]
    private static partial Regex KeyPattern();

    private readonly List<Status> _statuses = [];
    private readonly List<TaskType> _taskTypes = [];
    private readonly List<TaskItem> _tasks = [];
    private readonly List<StatusTransition> _transitions = [];

    public Guid Id { get; private set; }

    /// <summary>Короткий код доски в верхнем регистре, используется как префикс кода задачи (например "FLW").</summary>
    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    /// <summary>Счётчик для генерации следующего номера в коде задачи.</summary>
    public int NextTaskNumber { get; private set; }

    /// <summary>
    /// Потолок роли, которую глобальная роль даёт в этом проекте без участия (docs/TZ_project_access.md §1):
    /// null — роль в проекте равна глобальной; Viewer — «только чтение для всех, кроме участников».
    /// Глобальных Admin и Owner не ограничивает. Участие поднимает роль выше потолка.
    /// </summary>
    public ProjectRole? DefaultRole { get; private set; }

    /// <summary>Private — проект видят только участники и глобальные Admin/Owner (этап 4B); у старых проектов Open.</summary>
    public BoardVisibility Visibility { get; private set; }

    public IReadOnlyCollection<Status> Statuses => _statuses;

    /// <summary>Типы задач проекта, включая архивные (docs/TZ_task_model.md §1).</summary>
    public IReadOnlyCollection<TaskType> TaskTypes => _taskTypes;

    public IReadOnlyCollection<TaskItem> Tasks => _tasks;

    /// <summary>Free — статус меняется на любой; Restricted — только по <see cref="Transitions"/> (docs/TZ_workflow_config.md §2).</summary>
    public WorkflowMode WorkflowMode { get; private set; }

    /// <summary>Граф переходов. В режиме Free хранится (можно готовить заранее), но не применяется.</summary>
    public IReadOnlyCollection<StatusTransition> Transitions => _transitions;

    private Board()
    {
        // EF Core
    }

    private Board(string name, string key)
    {
        Id = Guid.NewGuid();
        Name = ValidateName(name);
        Key = ValidateKey(key);
        CreatedAt = DateTime.UtcNow;
        NextTaskNumber = 0;
    }

    /// <summary>
    /// Создаёт доску и сразу засеивает базовые наборы статусов (<see cref="DefaultStatuses"/>)
    /// и типов задач (<see cref="DefaultTaskTypes"/>).
    /// </summary>
    public static Board Create(string name, string key)
    {
        var board = new Board(name, key);

        foreach (var preset in DefaultStatuses.All)
            board.AddStatus(preset.Name, preset.Type, preset.IsInitial, preset.IsFinal);

        foreach (var preset in DefaultTaskTypes.All)
            board.AddTaskType(preset.Name, preset.Kind, preset.IsDefault);

        return board;
    }

    public void Rename(string name) => Name = ValidateName(name);

    public void SetVisibility(BoardVisibility visibility) =>
        Visibility = Enum.IsDefined(visibility)
            ? visibility
            : throw new ArgumentException($"Unknown board visibility {visibility}.", nameof(visibility));

    /// <summary>null — снять ограничение. Admin смысла не имеет (потолок выше любой производной роли) и не принимается.</summary>
    public void SetDefaultRole(ProjectRole? role)
    {
        if (role is { } r && (!Enum.IsDefined(r) || r == ProjectRole.Admin))
            throw new ArgumentException($"Default project role must be Viewer, Member or Developer, not {r}.", nameof(role));

        DefaultRole = role;
    }

    /// <summary>
    /// Добавляет статус в конец списка. Начальный — ровно один (перенести флаг — <see cref="SetInitialStatus"/>),
    /// финальных может быть несколько («Сделана», «Отменена»): статус закрывает задачу, если он финальный.
    /// Начальный не может быть финальным — иначе новые задачи рождались бы закрытыми.
    /// </summary>
    public Status AddStatus(string name, StatusType? type = null, bool isInitial = false, bool isFinal = false)
    {
        if (isInitial && _statuses.Any(s => s.IsInitial))
            throw new InvalidOperationException($"Board {Id} already has an initial status.");

        if (isInitial && isFinal)
            throw new InvalidOperationException("The initial status cannot be final.");

        if (type is { } t && !Enum.IsDefined(t))
            throw new ArgumentException($"Unknown status type {t}.", nameof(type));

        var normalized = Status.ValidateName(name);
        EnsureStatusNameFree(normalized, exceptId: null);

        var sortOrder = NextStatusSortOrder();
        var status = new Status(Id, normalized, sortOrder, isInitial, isFinal, type);
        _statuses.Add(status);
        return status;
    }

    /// <summary>Переносит флаг "начальный статус" на другой статус доски. Финальный начальным быть не может.</summary>
    public void SetInitialStatus(Guid statusId)
    {
        var target = GetStatus(statusId);
        if (target.IsFinal)
            throw new InvalidOperationException("A final status cannot be the initial one.");

        foreach (var status in _statuses.Where(s => s.IsInitial))
            status.SetInitial(false);

        target.SetInitial(true);
    }

    public void RenameStatus(Guid statusId, string name)
    {
        var status = GetStatus(statusId);
        var normalized = Status.ValidateName(name);
        EnsureStatusNameFree(normalized, exceptId: statusId);
        status.Rename(normalized);
    }

    /// <summary>Финальный статус закрывает задачу. Последний финальный снять нельзя, начальный финальным не сделать.</summary>
    public void SetStatusFinal(Guid statusId, bool isFinal)
    {
        var status = GetStatus(statusId);
        if (isFinal && status.IsInitial)
            throw new InvalidOperationException("The initial status cannot be final.");
        if (!isFinal && status.IsFinal && _statuses.Count(s => s.IsFinal) == 1)
            throw new InvalidOperationException("A board must keep at least one final status.");

        status.SetFinal(isFinal);
    }

    /// <summary>Вид статуса — общий для проектов ключ фильтров («в работе» во всех проектах); null — свой статус без вида.</summary>
    public void SetStatusType(Guid statusId, StatusType? type)
    {
        if (type is { } t && !Enum.IsDefined(t))
            throw new ArgumentException($"Unknown status type {t}.", nameof(type));

        GetStatus(statusId).SetType(type);
    }

    /// <summary>
    /// Новый порядок статусов — полный список Id проекта. SortOrder переписывается значениями выше текущего
    /// максимума: unique (BoardId, SortOrder) в БД проверяется построчно, и обмен двух значений на месте упёрся бы
    /// в него на первой же строке. Числа растут, но служат только для ORDER BY.
    /// </summary>
    public void ReorderStatuses(IReadOnlyList<Guid> statusIds)
    {
        if (statusIds.Count != _statuses.Count || statusIds.Distinct().Count() != statusIds.Count
            || statusIds.Any(id => _statuses.All(s => s.Id != id)))
            throw new ArgumentException("The new order must list every status of the board exactly once.", nameof(statusIds));

        var next = NextStatusSortOrder();
        for (var i = 0; i < statusIds.Count; i++)
            GetStatus(statusIds[i]).SetSortOrder(next + i);
    }

    /// <summary>
    /// Удаляет статус. Задачи из него хендлер переводит в <paramref name="moveTasksTo"/> заранее (со своим журналом):
    /// доска свои задачи не держит. Нельзя удалить начальный и последний финальный.
    /// </summary>
    public void RemoveStatus(Guid statusId, Guid moveTasksTo)
    {
        var status = GetStatus(statusId);
        if (moveTasksTo == statusId)
            throw new InvalidOperationException("Tasks must move to another status.");

        GetStatus(moveTasksTo);

        if (status.IsInitial)
            throw new InvalidOperationException("The initial status cannot be removed; make another status initial first.");
        if (status.IsFinal && _statuses.Count(s => s.IsFinal) == 1)
            throw new InvalidOperationException("The last final status cannot be removed.");

        _transitions.RemoveAll(t => t.FromStatusId == status.Id || t.ToStatusId == status.Id);
        _statuses.Remove(status);
    }

    public Status GetStatus(Guid statusId) =>
        _statuses.SingleOrDefault(s => s.Id == statusId)
        ?? throw new InvalidOperationException($"Status {statusId} does not belong to board {Id}.");

    /// <summary>
    /// Добавляет тип задачи. Имя уникально в проекте без учёта регистра; <paramref name="isDefault"/> переносит
    /// флаг «по умолчанию» на новый тип — он всегда ровно один.
    /// </summary>
    public TaskType AddTaskType(string name, TaskTypeKind kind, bool isDefault = false)
    {
        var normalized = TaskType.ValidateName(name);
        EnsureTaskTypeNameFree(normalized, exceptId: null);

        var sortOrder = _taskTypes.Count == 0 ? 0 : _taskTypes.Max(t => t.SortOrder) + 1;
        // Первый тип становится типом по умолчанию сам: иначе CreateTask без типа было бы не во что положить.
        var type = new TaskType(Id, normalized, kind, sortOrder, isDefault || _taskTypes.Count == 0);

        if (type.IsDefault)
            foreach (var other in _taskTypes.Where(t => t.IsDefault))
                other.SetDefault(false);

        _taskTypes.Add(type);
        return type;
    }

    public void RenameTaskType(Guid typeId, string name)
    {
        var type = GetTaskType(typeId);
        var normalized = TaskType.ValidateName(name);
        EnsureTaskTypeNameFree(normalized, exceptId: typeId);
        type.Rename(normalized);
    }

    /// <summary>Переносит флаг «по умолчанию» на другой тип. Архивный тип им быть не может.</summary>
    public void SetDefaultTaskType(Guid typeId)
    {
        var target = GetTaskType(typeId);
        if (target.IsArchived)
            throw new InvalidOperationException("An archived task type cannot be the default one.");

        foreach (var type in _taskTypes.Where(t => t.IsDefault))
            type.SetDefault(false);

        target.SetDefault(true);
    }

    /// <summary>
    /// Архивирует или возвращает тип. Задачи архивного типа его сохраняют, новые на нём не создаются.
    /// Тип по умолчанию архивировать нельзя — сначала назначить другой.
    /// </summary>
    public void SetTaskTypeArchived(Guid typeId, bool isArchived)
    {
        var type = GetTaskType(typeId);
        if (isArchived && type.IsDefault)
            throw new InvalidOperationException("The default task type cannot be archived; choose another default first.");

        type.SetArchived(isArchived);
    }

    public TaskType GetTaskType(Guid typeId) =>
        _taskTypes.SingleOrDefault(t => t.Id == typeId)
        ?? throw new InvalidOperationException($"Task type {typeId} does not belong to board {Id}.");

    /// <summary>
    /// Создаёт задачу. Если <paramref name="statusId"/> не передан — задача уходит в статус доски
    /// с <see cref="Status.IsInitial"/> = true (по умолчанию "Не начата"). <paramref name="createdById"/> — actor
    /// (User.Id), пишется в <see cref="TaskItem.CreatedById"/>. Без <paramref name="typeId"/> — тип проекта
    /// по умолчанию; архивный тип не принимается.
    /// </summary>
    /// <remarks>
    /// <paramref name="rank"/> — ключ ручного порядка; задачи проекта агрегат не держит, поэтому «в конец проекта»
    /// вычисляет хендлер по максимальному рангу в БД. null — <see cref="Ranking.FractionalIndex.First"/> (пустой проект, тесты).
    /// </remarks>
    public TaskItem CreateTask(string title, string? description = null, Guid? statusId = null, Guid? createdById = null, Guid? typeId = null, string? rank = null)
    {
        var type = typeId is null
            ? _taskTypes.SingleOrDefault(t => t.IsDefault)
              ?? throw new InvalidOperationException($"Board {Id} has no default task type configured.")
            : GetTaskType(typeId.Value);

        if (type.IsArchived)
            throw new InvalidOperationException($"Task type {type.Id} is archived.");

        Guid resolvedStatusId;
        if (statusId is null)
        {
            var initial = _statuses.SingleOrDefault(s => s.IsInitial)
                ?? throw new InvalidOperationException($"Board {Id} has no initial status configured.");
            resolvedStatusId = initial.Id;
        }
        else
        {
            if (_statuses.All(s => s.Id != statusId.Value))
                throw new InvalidOperationException($"Status {statusId.Value} does not belong to board {Id}.");

            // В Restricted сразу в неначальный статус — только если в него есть переход «из любого»: иначе создание
            // в колонку канбана «В работе» обходило бы граф.
            var initialId = _statuses.SingleOrDefault(s => s.IsInitial)?.Id;
            if (WorkflowMode == WorkflowMode.Restricted && statusId != initialId
                && !_transitions.Any(t => t.FromStatusId is null && t.ToStatusId == statusId.Value))
                throw new InvalidOperationException($"В статус «{StatusName(statusId.Value)}» задачу нельзя создать сразу: в workflow нет перехода в него «из любого».");

            resolvedStatusId = statusId.Value;
        }

        // Ранг проверяем до того, как счётчик номеров сдвинется: отказ не должен сжигать номер задачи.
        rank ??= Ranking.FractionalIndex.First;
        Ranking.FractionalIndex.Validate(rank);

        // DESK-1
        NextTaskNumber++;
        
        var code = TaskCode.Create(Key, NextTaskNumber);
        var task = new TaskItem(Id, code, title, description, resolvedStatusId, createdById, type.Id, rank);
        _tasks.Add(task);
        return task;
    }

    // ---- Workflow (docs/TZ_workflow_config.md §2) ----

    /// <summary>
    /// Заменяет workflow целиком: режим и переходы (редактор-матрица присылает всё сразу, поэтому частичных правок нет).
    /// Статусы — из этого проекта, переход в тот же статус не нужен, пары (из, в) не повторяются. В Restricted
    /// у каждого нефинального статуса должен быть исходящий переход, иначе задача в нём застрянет — отказ перечисляет
    /// тупиковые статусы.
    /// </summary>
    public void SetWorkflow(WorkflowMode mode, IReadOnlyList<TransitionSpec> transitions)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentException($"Unknown workflow mode {mode}.", nameof(mode));
        ArgumentNullException.ThrowIfNull(transitions);

        foreach (var t in transitions)
        {
            if (t.FromStatusId is { } from && _statuses.All(s => s.Id != from))
                throw new InvalidOperationException($"Status {from} does not belong to board {Id}.");
            if (_statuses.All(s => s.Id != t.ToStatusId))
                throw new InvalidOperationException($"Status {t.ToStatusId} does not belong to board {Id}.");
            if (t.FromStatusId == t.ToStatusId)
                throw new InvalidOperationException("A transition to the same status is not needed.");
        }

        if (transitions.GroupBy(t => (t.FromStatusId, t.ToStatusId)).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Each transition (from, to) may appear only once.");

        var next = transitions.Select(t => new StatusTransition(Id, t.FromStatusId, t.ToStatusId, t.Name, t.Conditions ?? TransitionConditions.None)).ToList();
        if (mode == WorkflowMode.Restricted)
        {
            var deadEnds = DeadEnds(next);
            if (deadEnds.Count > 0)
                throw new InvalidOperationException(
                    $"Из статусов {string.Join(", ", deadEnds.Select(s => $"«{s.Name}»"))} нет ни одного перехода: задачи в них застрянут.");
        }

        _transitions.Clear();
        _transitions.AddRange(next);
        WorkflowMode = mode;
    }

    /// <summary>Нефинальные статусы, из которых нет ни одного перехода (с учётом переходов «из любого»).</summary>
    public IReadOnlyList<Status> DeadEnds() => DeadEnds(_transitions);

    private List<Status> DeadEnds(IReadOnlyCollection<StatusTransition> transitions) =>
        _statuses
            .Where(s => !s.IsFinal)
            .Where(s => !transitions.Any(t => (t.FromStatusId == s.Id || t.FromStatusId is null) && t.ToStatusId != s.Id))
            .OrderBy(s => s.SortOrder)
            .ToList();

    /// <summary>
    /// Можно ли перевести задачу из <paramref name="fromStatusId"/> в <paramref name="toStatusId"/>. Free — всегда.
    /// Restricted — нужен переход графа (прямой или «из любого»), чьи условия выполнены; если подходящих переходов
    /// несколько, хватает одного. Причины отказа — по первому переходу: человеку важнее, чего не хватает, чем все
    /// варианты сразу.
    /// </summary>
    public TransitionCheck CheckTransition(Guid fromStatusId, Guid toStatusId, TransitionContext context)
    {
        if (fromStatusId == toStatusId || WorkflowMode == WorkflowMode.Free)
            return TransitionCheck.Ok;

        var candidates = _transitions
            .Where(t => t.ToStatusId == toStatusId && (t.FromStatusId == fromStatusId || t.FromStatusId is null))
            .OrderBy(t => t.FromStatusId is null ? 1 : 0)
            .ToList();
        if (candidates.Count == 0)
            return TransitionCheck.Denied($"Перехода «{StatusName(fromStatusId)}» → «{StatusName(toStatusId)}» в workflow проекта нет");

        List<string>? firstReasons = null;
        foreach (var transition in candidates)
        {
            var reasons = FailedConditions(transition.Conditions, context);
            if (reasons.Count == 0)
                return TransitionCheck.Ok;
            firstReasons ??= reasons;
        }

        return new TransitionCheck(false, firstReasons!);
    }

    private static List<string> FailedConditions(TransitionConditions c, TransitionContext ctx)
    {
        var reasons = new List<string>();
        if (c.MinRole is { } role && ctx.ActorRole < role)
            reasons.Add($"Переход доступен роли «{RoleLabel(role)}» и выше");
        if (c.RequireAssignee && !ctx.HasAssignee)
            reasons.Add("Сначала назначьте исполнителя");
        if (c.RequireChildrenDone && !ctx.ChildrenDone)
            reasons.Add("Не все подзадачи закрыты");
        if (c.RequireChecklistDone && !ctx.ChecklistDone)
            reasons.Add("Чек-лист выполнен не полностью");
        return reasons;
    }

    private static string RoleLabel(ProjectRole role) => role switch
    {
        ProjectRole.Viewer => "Читатель",
        ProjectRole.Member => "Участник",
        ProjectRole.Developer => "Разработчик",
        _ => "Администратор"
    };

    private string StatusName(Guid id) => _statuses.FirstOrDefault(s => s.Id == id)?.Name ?? "удалённый статус";

    private int NextStatusSortOrder() => _statuses.Count == 0 ? 0 : _statuses.Max(s => s.SortOrder) + 1;

    private void EnsureStatusNameFree(string name, Guid? exceptId)
    {
        if (_statuses.Any(s => s.Id != exceptId && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Status \"{name}\" already exists on board {Id}.");
    }

    private void EnsureTaskTypeNameFree(string name, Guid? exceptId)
    {
        if (_taskTypes.Any(t => t.Id != exceptId && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Task type \"{name}\" already exists on board {Id}.");
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Board name must not be empty.", nameof(name));

        return name.Trim();
    }

    private static string ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Board key must not be empty.", nameof(key));

        var normalized = key.Trim().ToUpperInvariant();
        if (!KeyPattern().IsMatch(normalized))
            throw new ArgumentException("Board key must match ^[A-Z][A-Z0-9]{1,9}$.", nameof(key));

        return normalized;
    }
}
