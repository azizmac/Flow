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
    private readonly List<TaskItem> _tasks = [];

    public Guid Id { get; private set; }

    /// <summary>Короткий код доски в верхнем регистре, используется как префикс кода задачи (например "FLW").</summary>
    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    /// <summary>Счётчик для генерации следующего номера в коде задачи.</summary>
    public int NextTaskNumber { get; private set; }

    public IReadOnlyCollection<Status> Statuses => _statuses;

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

    /// <summary>Создаёт доску и сразу засеивает базовый набор статусов задач из <see cref="DefaultStatuses"/>.</summary>
    public static Board Create(string name, string key)
    {
        var board = new Board(name, key);

        foreach (var preset in DefaultStatuses.All)
            board.AddStatus(preset.Name, preset.Type, preset.IsInitial, preset.IsFinal);

        return board;
    }

    public void Rename(string name) => Name = ValidateName(name);

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
    /// Создаёт задачу. Если <paramref name="statusId"/> не передан — задача уходит в статус доски
    /// с <see cref="Status.IsInitial"/> = true (по умолчанию "Не начата").
    /// </summary>
    public TaskItem CreateTask(string title, string? description = null, Guid? statusId = null)
    {
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

        NextTaskNumber++;
        var code = TaskCode.Create(Key, NextTaskNumber);
        var task = new TaskItem(Id, code, title, description, resolvedStatusId);
        _tasks.Add(task);
        return task;
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
