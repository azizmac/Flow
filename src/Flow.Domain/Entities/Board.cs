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

    /// <summary>Добавляет статус задачи на доску. На доске может быть максимум один начальный и один финальный статус.</summary>
    public Status AddStatus(string name, StatusType? type = null, bool isInitial = false, bool isFinal = false)
    {
        if (isInitial && _statuses.Any(s => s.IsInitial))
            throw new InvalidOperationException($"Board {Id} already has an initial status.");

        if (isFinal && _statuses.Any(s => s.IsFinal))
            throw new InvalidOperationException($"Board {Id} already has a final status.");

        var sortOrder = _statuses.Count == 0 ? 0 : _statuses.Max(s => s.SortOrder) + 1;
        var status = new Status(Id, name, sortOrder, isInitial, isFinal, type);
        _statuses.Add(status);
        return status;
    }

    /// <summary>Переносит флаг "начальный статус" на другой статус доски. Название статуса при этом не важно.</summary>
    public void SetInitialStatus(Guid statusId)
    {
        var target = _statuses.SingleOrDefault(s => s.Id == statusId)
            ?? throw new InvalidOperationException($"Status {statusId} does not belong to board {Id}.");

        foreach (var status in _statuses.Where(s => s.IsInitial))
            status.SetInitial(false);

        target.SetInitial(true);
    }

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
    public TaskItem CreateTask(string title, string? description = null, Guid? statusId = null, Guid? createdById = null, Guid? typeId = null)
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
            resolvedStatusId = statusId.Value;
        }

        // DESK-1
        NextTaskNumber++;
        
        var code = TaskCode.Create(Key, NextTaskNumber);
        var task = new TaskItem(Id, code, title, description, resolvedStatusId, createdById, type.Id);
        _tasks.Add(task);
        return task;
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
