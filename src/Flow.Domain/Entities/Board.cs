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
    private readonly List<CustomFieldDefinition> _customFields = [];
    private readonly List<TaskScreen> _screens = [];

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

    /// <summary>Пользовательские поля проекта, включая архивные (docs/TZ_task_model.md §4).</summary>
    public IReadOnlyCollection<CustomFieldDefinition> CustomFields => _customFields;

    /// <summary>Настроенные экраны задач (docs/TZ_workflow_config.md §3); нет подходящего — встроенный.</summary>
    public IReadOnlyCollection<TaskScreen> Screens => _screens;

    /// <summary>Free — статус меняется на любой; Restricted — только по <see cref="Transitions"/> (docs/TZ_workflow_config.md §2).</summary>
    public WorkflowMode WorkflowMode { get; private set; }

    /// <summary>Граф переходов. В режиме Free хранится (можно готовить заранее), но не применяется.</summary>
    public IReadOnlyCollection<StatusTransition> Transitions => _transitions;

    /// <summary>
    /// Финальная колонка канбана показывает задачи, закрытые за столько последних дней (docs/TZ_task_views.md §1):
    /// иначе через полгода колонка «Сделана» грузила бы тысячи карточек. Список задач это окно не трогает.
    /// </summary>
    public int DoneColumnDays { get; private set; } = DefaultDoneColumnDays;

    public const int DefaultDoneColumnDays = 14;
    public const int MaxDoneColumnDays = 365;

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

    /// <summary>Мягкий WIP-лимит колонки; null снимает.</summary>
    public void SetStatusWipLimit(Guid statusId, int? limit) => GetStatus(statusId).SetWipLimit(limit);

    public void SetDoneColumnDays(int days)
    {
        if (days is < 1 or > MaxDoneColumnDays)
            throw new ArgumentException($"Done column window must be between 1 and {MaxDoneColumnDays} days.", nameof(days));

        DoneColumnDays = days;
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

    // ---- пользовательские поля (docs/TZ_task_model.md §4) ----

    /// <summary>
    /// Новое поле в конце списка. Key уникален в проекте (и среди архивных — по нему FQL находит поле) и потом не
    /// меняется; у Select/MultiSelect нужен хотя бы один вариант, у остальных вариантов нет. Типы задач — свои.
    /// </summary>
    public CustomFieldDefinition AddCustomField(
        string key, string name, CustomFieldType type, IEnumerable<string>? options = null, bool isRequired = false, IEnumerable<Guid>? taskTypeIds = null)
    {
        var normalizedKey = CustomFieldDefinition.ValidateKey(key);
        if (_customFields.Any(f => f.Key == normalizedKey))
            throw new InvalidOperationException($"Поле с ключом «{normalizedKey}» в проекте уже есть.");

        var field = new CustomFieldDefinition(Id, normalizedKey, name, type, _customFields.Count == 0 ? 0 : _customFields.Max(f => f.SortOrder) + 1);
        EnsureCustomFieldNameFree(field.Name, exceptId: null);

        var labels = options?.ToList() ?? [];
        if (field.HasOptions)
            field.SetOptions(labels.Select(l => ((Guid?)null, l, (string?)null)));
        else if (labels.Count > 0)
            throw new InvalidOperationException($"У поля типа {type} вариантов не бывает.");

        field.SetRequired(isRequired);
        field.SetTaskTypes(ValidateTaskTypes(taskTypeIds));
        _customFields.Add(field);
        return field;
    }

    /// <summary>PATCH поля: null — не трогать. Тип и ключ не меняются: значения задач уже записаны в своей форме.</summary>
    public CustomFieldDefinition UpdateCustomField(
        Guid fieldId,
        string? name = null,
        IEnumerable<(Guid? Id, string Label, string? Color)>? options = null,
        bool? isRequired = null,
        IEnumerable<Guid>? taskTypeIds = null,
        bool? isArchived = null)
    {
        var field = GetCustomField(fieldId);
        if (name is not null)
        {
            var normalized = CustomFieldDefinition.ValidateName(name);
            EnsureCustomFieldNameFree(normalized, fieldId);
            field.Rename(normalized);
        }
        if (options is not null)
            field.SetOptions(options);
        if (isRequired is { } required)
            field.SetRequired(required);
        if (taskTypeIds is not null)
            field.SetTaskTypes(ValidateTaskTypes(taskTypeIds));
        if (isArchived is { } archived)
            field.SetArchived(archived);
        return field;
    }

    /// <summary>Полная перестановка полей проекта.</summary>
    public void ReorderCustomFields(IReadOnlyList<Guid> fieldIds)
    {
        if (fieldIds.Count != _customFields.Count || fieldIds.Distinct().Count() != fieldIds.Count
            || fieldIds.Any(id => _customFields.All(f => f.Id != id)))
            throw new ArgumentException("The new order must list every custom field of the board exactly once.", nameof(fieldIds));

        for (var i = 0; i < fieldIds.Count; i++)
            GetCustomField(fieldIds[i]).SetSortOrder(i);
    }

    public CustomFieldDefinition GetCustomField(Guid fieldId) =>
        _customFields.SingleOrDefault(f => f.Id == fieldId)
        ?? throw new InvalidOperationException($"Custom field {fieldId} does not belong to board {Id}.");

    /// <summary>
    /// Обязательные поля типа <paramref name="typeId"/>, которых у задачи нет. Проверяется при создании и смене типа;
    /// старые задачи без значения остаются валидными — поле могло стать обязательным после них.
    /// </summary>
    public IReadOnlyList<CustomFieldDefinition> MissingRequiredFields(TaskItem task, Guid typeId) =>
        _customFields
            .Where(f => f.IsRequired && f.AppliesTo(typeId) && task.GetCustomField(f.Id) is null)
            .OrderBy(f => f.SortOrder)
            .ToList();

    // ---- экраны задач (docs/TZ_workflow_config.md §3) ----

    /// <summary>
    /// Заменяет экран (тип или null — для всех типов, контекст) списком полей. Поле — системное из
    /// <see cref="ScreenFields.System"/> или пользовательское поле этого проекта, без повторов; на Create — только то,
    /// что заполняется при создании. Название и статус есть всегда и на экран не выносятся.
    /// </summary>
    public TaskScreen SetScreen(Guid? taskTypeId, ScreenContext context, IReadOnlyList<ScreenField> fields)
    {
        if (!Enum.IsDefined(context))
            throw new ArgumentException($"Unknown screen context {context}.", nameof(context));
        if (taskTypeId is { } typeId)
            GetTaskType(typeId);

        var seen = new HashSet<string>();
        foreach (var field in fields)
        {
            if (!seen.Add(field.Field))
                throw new InvalidOperationException($"Поле «{field.Field}» на экране дважды.");

            if (field.Field.StartsWith(ScreenFields.SystemPrefix, StringComparison.Ordinal))
            {
                var name = field.Field[ScreenFields.SystemPrefix.Length..];
                if (!ScreenFields.System.Contains(name))
                    throw new InvalidOperationException($"Системного поля «{name}» нет.");
                if (context == ScreenContext.Create && !ScreenFields.OnCreate.Contains(name))
                    throw new InvalidOperationException($"Поле «{ScreenFields.Label(name)}» не заполняется при создании задачи.");
            }
            else if (!field.Field.StartsWith(ScreenFields.CustomPrefix, StringComparison.Ordinal)
                     || !Guid.TryParse(field.Field[ScreenFields.CustomPrefix.Length..], out var fieldId))
                throw new InvalidOperationException($"Неизвестное поле «{field.Field}».");
            else
                GetCustomField(fieldId);
        }

        var screen = _screens.FirstOrDefault(s => s.TaskTypeId == taskTypeId && s.Context == context);
        if (screen is null)
        {
            screen = new TaskScreen(Id, taskTypeId, context);
            _screens.Add(screen);
        }

        screen.SetFields(fields);
        return screen;
    }

    /// <summary>Убирает настройку — снова действует экран «для всех типов» или встроенный.</summary>
    public void ResetScreen(Guid? taskTypeId, ScreenContext context) =>
        _screens.RemoveAll(s => s.TaskTypeId == taskTypeId && s.Context == context);

    /// <summary>Поля экрана задачи этого типа: свой экран типа → экран «для всех типов» → встроенный.</summary>
    public IReadOnlyList<ScreenField> ResolveScreen(Guid taskTypeId, ScreenContext context) =>
        (_screens.FirstOrDefault(s => s.TaskTypeId == taskTypeId && s.Context == context)
         ?? _screens.FirstOrDefault(s => s.TaskTypeId is null && s.Context == context))?.Fields
        ?? ScreenFields.Default(this, taskTypeId, context);

    /// <summary>
    /// Обязательные поля экрана создания, не заполненные у новой задачи — подписи для ошибки. Обязательное
    /// пользовательское поле проверяет <see cref="MissingRequiredFields"/> и без экрана.
    /// </summary>
    public IReadOnlyList<string> MissingOnCreateScreen(TaskItem task)
    {
        var missing = new List<string>();
        foreach (var field in ResolveScreen(task.TypeId, ScreenContext.Create).Where(f => f.Required))
        {
            if (field.Field.StartsWith(ScreenFields.CustomPrefix, StringComparison.Ordinal))
            {
                var id = Guid.Parse(field.Field[ScreenFields.CustomPrefix.Length..]);
                if (task.GetCustomField(id) is null && _customFields.FirstOrDefault(f => f.Id == id) is { } custom && custom.AppliesTo(task.TypeId))
                    missing.Add(custom.Name);
                continue;
            }

            var name = field.Field[ScreenFields.SystemPrefix.Length..];
            var empty = name switch
            {
                "priority" => task.Priority == TaskPriority.None,
                "assignee" => task.AssigneeId is null,
                "description" => string.IsNullOrWhiteSpace(task.Description),
                _ => false
            };
            if (empty)
                missing.Add(ScreenFields.Label(name));
        }

        return missing;
    }

    private List<Guid> ValidateTaskTypes(IEnumerable<Guid>? taskTypeIds)
    {
        var ids = taskTypeIds?.Distinct().ToList() ?? [];
        foreach (var id in ids)
            GetTaskType(id);
        return ids;
    }

    private void EnsureCustomFieldNameFree(string name, Guid? exceptId)
    {
        if (_customFields.Any(f => f.Id != exceptId && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Поле «{name}» в проекте уже есть.");
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
            if (WorkflowModeFor(type.Id) == WorkflowMode.Restricted && statusId != initialId
                && !TransitionsFor(type.Id).Any(t => t.FromStatusId is null && t.ToStatusId == statusId.Value))
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

    /// <summary>
    /// Принимает задачу из другого проекта (docs/TZ_task_model.md §6): новый код из счётчика этого проекта, статус
    /// и тип — этого проекта (тип не архивный). Статус и тип подбирает хендлер по картам и видам; workflow здесь
    /// не проверяется — перенос служебный, как перевод задач при удалении статуса. Возвращает прежний код.
    /// </summary>
    public TaskCode ReceiveTask(TaskItem task, Guid statusId, Guid typeId, string rank, string customFieldsJson, bool keepParent)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.BoardId == Id)
            throw new InvalidOperationException("Задача уже в этом проекте.");
        if (_statuses.All(s => s.Id != statusId))
            throw new InvalidOperationException($"Status {statusId} does not belong to board {Id}.");
        if (GetTaskType(typeId).IsArchived)
            throw new InvalidOperationException($"Task type {typeId} is archived.");
        Ranking.FractionalIndex.Validate(rank);

        var old = task.Code;
        NextTaskNumber++;
        task.Relocate(Id, TaskCode.Create(Key, NextTaskNumber), statusId, typeId, rank, customFieldsJson, keepParent);
        return old;
    }

    // ---- Workflow (docs/TZ_workflow_config.md §2) ----

    /// <summary>
    /// Заменяет workflow целиком: режим и переходы (редактор-матрица присылает всё сразу, поэтому частичных правок нет).
    /// Статусы — из этого проекта, переход в тот же статус не нужен, пары (из, в) не повторяются. В Restricted
    /// у каждого нефинального статуса должен быть исходящий переход, иначе задача в нём застрянет — отказ перечисляет
    /// тупиковые статусы.
    /// </summary>
    /// <summary>
    /// Раскладка графа workflow (этап 3D): позиции узлов своих статусов, 0…5000 по каждой оси. Пустой список — вернуть
    /// автораскладку всем; статус, не попавший в непустой список, получает автораскладку (новый статус не должен
    /// оказаться поверх других).
    /// </summary>
    public void SetStatusLayout(IReadOnlyCollection<(Guid StatusId, double X, double Y)> positions)
    {
        foreach (var (statusId, x, y) in positions)
        {
            if (_statuses.All(s => s.Id != statusId))
                throw new InvalidOperationException($"Status {statusId} does not belong to board {Id}.");
            if (double.IsNaN(x) || double.IsNaN(y) || x < 0 || y < 0 || x > Status.MaxGraphCoordinate || y > Status.MaxGraphCoordinate)
                throw new ArgumentException($"Координаты узла — от 0 до {Status.MaxGraphCoordinate}.", nameof(positions));
        }

        var byId = positions.GroupBy(p => p.StatusId).ToDictionary(g => g.Key, g => g.Last());
        foreach (var status in _statuses)
        {
            if (byId.TryGetValue(status.Id, out var p))
                status.SetGraphPosition(Math.Round(p.X, 1), Math.Round(p.Y, 1));
            else
                status.SetGraphPosition(null, null);
        }
    }

    /// <summary>Режим workflow, по которому живут задачи типа: свой у типа (этап 3E) или проекта.</summary>
    public WorkflowMode WorkflowModeFor(Guid? taskTypeId) =>
        taskTypeId is { } id && _taskTypes.FirstOrDefault(t => t.Id == id)?.OwnWorkflowMode is { } own ? own : WorkflowMode;

    /// <summary>Есть ли у типа свой workflow.</summary>
    public bool HasOwnWorkflow(Guid taskTypeId) => _taskTypes.FirstOrDefault(t => t.Id == taskTypeId)?.OwnWorkflowMode is not null;

    /// <summary>Переходы workflow, по которому живут задачи типа: свои у типа или проекта (TaskTypeId = null).</summary>
    public IReadOnlyList<StatusTransition> TransitionsFor(Guid? taskTypeId)
    {
        var owner = taskTypeId is { } id && HasOwnWorkflow(id) ? taskTypeId : null;
        return _transitions.Where(t => t.TaskTypeId == owner).ToList();
    }

    /// <summary>
    /// Заменить workflow целиком: проекта (taskTypeId = null) или свой у типа задачи (этап 3E) — тогда тип перестаёт
    /// жить по workflow проекта. Статусы общие для всех workflow проекта.
    /// </summary>
    public void SetWorkflow(WorkflowMode mode, IReadOnlyList<TransitionSpec> transitions, Guid? taskTypeId = null)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentException($"Unknown workflow mode {mode}.", nameof(mode));
        ArgumentNullException.ThrowIfNull(transitions);
        var type = taskTypeId is { } typeId
            ? _taskTypes.FirstOrDefault(t => t.Id == typeId) ?? throw new InvalidOperationException($"Task type {typeId} does not belong to board {Id}.")
            : null;

        foreach (var t in transitions)
        {
            if (t.FromStatusId is { } from && _statuses.All(s => s.Id != from))
                throw new InvalidOperationException($"Status {from} does not belong to board {Id}.");
            if (_statuses.All(s => s.Id != t.ToStatusId))
                throw new InvalidOperationException($"Status {t.ToStatusId} does not belong to board {Id}.");
            if (t.FromStatusId == t.ToStatusId)
                throw new InvalidOperationException("A transition to the same status is not needed.");
            foreach (var fieldId in t.Conditions?.RequireFields ?? [])
                GetCustomField(fieldId);
        }

        if (transitions.GroupBy(t => (t.FromStatusId, t.ToStatusId)).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Each transition (from, to) may appear only once.");

        var next = transitions.Select(t => new StatusTransition(Id, t.FromStatusId, t.ToStatusId, t.Name, t.Conditions ?? TransitionConditions.None, type?.Id)).ToList();
        if (mode == WorkflowMode.Restricted)
        {
            var deadEnds = DeadEnds(next);
            if (deadEnds.Count > 0)
                throw new InvalidOperationException(
                    $"Из статусов {string.Join(", ", deadEnds.Select(s => $"«{s.Name}»"))} нет ни одного перехода: задачи в них застрянут.");
        }

        _transitions.RemoveAll(t => t.TaskTypeId == type?.Id);
        _transitions.AddRange(next);
        if (type is null)
            WorkflowMode = mode;
        else
            type.SetOwnWorkflowMode(mode);
    }

    /// <summary>Тип снова живёт по workflow проекта: свои переходы типа удаляются (этап 3E).</summary>
    public void ResetTypeWorkflow(Guid taskTypeId)
    {
        var type = _taskTypes.FirstOrDefault(t => t.Id == taskTypeId)
                   ?? throw new InvalidOperationException($"Task type {taskTypeId} does not belong to board {Id}.");
        _transitions.RemoveAll(t => t.TaskTypeId == type.Id);
        type.SetOwnWorkflowMode(null);
    }

    /// <summary>Нефинальные статусы, из которых нет ни одного перехода (с учётом переходов «из любого») в workflow типа или проекта.</summary>
    public IReadOnlyList<Status> DeadEnds(Guid? taskTypeId = null) => DeadEnds(TransitionsFor(taskTypeId));

    private List<Status> DeadEnds(IReadOnlyCollection<StatusTransition> transitions) =>
        _statuses
            .Where(s => !s.IsFinal)
            .Where(s => !transitions.Any(t => (t.FromStatusId == s.Id || t.FromStatusId is null) && t.ToStatusId != s.Id))
            .OrderBy(s => s.SortOrder)
            .ToList();

    /// <summary>
    /// Можно ли перевести задачу из <paramref name="fromStatusId"/> в <paramref name="toStatusId"/> — по workflow её типа
    /// (свой у типа или проекта, этап 3E). Free — всегда. Restricted — нужен переход графа (прямой или «из любого»), чьи
    /// условия выполнены; если подходящих несколько, хватает одного. Причины отказа — по первому переходу: человеку
    /// важнее, чего не хватает, чем все варианты сразу.
    /// </summary>
    public TransitionCheck CheckTransition(Guid fromStatusId, Guid toStatusId, TransitionContext context, Guid? taskTypeId = null)
    {
        if (fromStatusId == toStatusId || WorkflowModeFor(taskTypeId) == WorkflowMode.Free)
            return TransitionCheck.Ok;

        var candidates = TransitionsFor(taskTypeId)
            .Where(t => t.ToStatusId == toStatusId && (t.FromStatusId == fromStatusId || t.FromStatusId is null))
            .OrderBy(t => t.FromStatusId is null ? 1 : 0)
            .ToList();
        if (candidates.Count == 0)
            return TransitionCheck.Denied(taskTypeId is { } typeId && HasOwnWorkflow(typeId)
                ? $"Перехода «{StatusName(fromStatusId)}» → «{StatusName(toStatusId)}» в workflow типа «{_taskTypes.First(t => t.Id == typeId).Name}» нет"
                : $"Перехода «{StatusName(fromStatusId)}» → «{StatusName(toStatusId)}» в workflow проекта нет");

        List<string>? firstReasons = null;
        foreach (var transition in candidates)
        {
            var reasons = FailedConditions(transition, context);
            if (reasons.Count == 0)
                return TransitionCheck.Ok;
            firstReasons ??= reasons;
        }

        return new TransitionCheck(false, firstReasons!);
    }

    private List<string> FailedConditions(StatusTransition transition, TransitionContext ctx)
    {
        var c = transition.Conditions;
        var reasons = new List<string>();
        if (c.MinRole is { } role && ctx.ActorRole < role)
            reasons.Add($"Переход доступен роли «{RoleLabel(role)}» и выше");
        if (c.RequireAssignee && !ctx.HasAssignee)
            reasons.Add("Сначала назначьте исполнителя");
        if (c.RequireChildrenDone && !ctx.ChildrenDone)
            reasons.Add("Не все подзадачи закрыты");
        if (c.RequireChecklistDone && !ctx.ChecklistDone)
            reasons.Add("Чек-лист выполнен не полностью");
        foreach (var fieldId in c.RequireFields ?? [])
            if (ctx.FilledFields is not { } filled || !filled.Contains(fieldId))
                reasons.Add($"Заполните поле «{_customFields.FirstOrDefault(f => f.Id == fieldId)?.Name ?? "удалённое поле"}»");
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
