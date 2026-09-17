using System.Linq.Expressions;
using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Tasks;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class TaskItemRepository(FlowDbContext db) : ITaskItemRepository
{
    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.TaskItems.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(Guid boardId, Guid? assigneeId, CancellationToken cancellationToken) =>
        await db.TaskItems
            .Where(t => t.BoardId == boardId)
            .Where(t => assigneeId == null || t.AssigneeId == assigneeId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> SearchAsync(TaskListFilter filter, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);

        // Offset — для таблицы со страницами и сортировкой по колонке.
        if (filter.Offset is { } offset)
        {
            return await Ordered(query, filter)
                .Skip(offset)
                .Take(filter.Limit)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        // Keyset вместо OFFSET: страницы не разъезжаются, когда во время листания добавляют задачу.
        // Он работает только с порядком по умолчанию — курсор кодирует именно пару (CreatedAt, Id).
        if (filter.BeforeCreatedAt is { } at && filter.BeforeId is { } id)
            query = query.Where(t => t.CreatedAt < at || (t.CreatedAt == at && t.Id.CompareTo(id) < 0));

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(filter.Limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Порядок для offset-режима. Навигационных свойств у TaskItem нет (только идентификаторы),
    /// поэтому соседние таблицы подтягиваются подзапросом — по одному скалярному на строку сортировки.
    /// Код задачи хранится строкой ("WEB-42"), и лексикографически WEB-10 встал бы перед WEB-2; номера
    /// внутри проекта выдаются последовательно, поэтому вместо разбора строки сортируем по времени
    /// создания внутри ключа проекта — порядок тот же, а запрос остаётся простым.
    /// Id в конце — чтобы страницы не разъезжались при одинаковых значениях (импорт создаёт задачи пачкой).
    /// </summary>
    private IQueryable<TaskItem> Ordered(IQueryable<TaskItem> query, TaskListFilter filter)
    {
        var desc = filter.Descending;

        IOrderedQueryable<TaskItem> ordered = filter.Sort switch
        {
            // Внутри проекта разворачиваем и номера: при обратной сортировке ожидается WEB-9, WEB-8, …
            TaskSortField.Code => Order(
                Order(query, t => db.Boards.Where(b => b.Id == t.BoardId).Select(b => b.Key).FirstOrDefault(), desc),
                t => t.CreatedAt, desc),
            TaskSortField.Title => Order(query, t => t.Title, desc),
            TaskSortField.Status => Order(query, t => db.Statuses.Where(s => s.Id == t.StatusId).Select(s => s.SortOrder).FirstOrDefault(), desc),
            // Без исполнителя — в конец при любом направлении: пустые строки иначе всплывали бы наверх.
            TaskSortField.Assignee => Order(
                query.OrderBy(t => t.AssigneeId == null),
                t => db.Users.Where(u => u.Id == t.AssigneeId).Select(u => u.LastName + " " + u.FirstName).FirstOrDefault(),
                desc),
            TaskSortField.Due => Order(query.OrderBy(t => t.DueDate == null), t => t.DueDate, desc),
            _ => Order(query, t => t.CreatedAt, desc)
        };

        return ordered.ThenBy(t => t.Id);
    }

    private static IOrderedQueryable<TaskItem> Order<TKey>(IQueryable<TaskItem> query, Expression<Func<TaskItem, TKey>> key, bool desc) =>
        desc ? query.OrderByDescending(key) : query.OrderBy(key);

    private static IOrderedQueryable<TaskItem> Order<TKey>(IOrderedQueryable<TaskItem> query, Expression<Func<TaskItem, TKey>> key, bool desc) =>
        desc ? query.ThenByDescending(key) : query.ThenBy(key);

    public async Task<TaskCounts> CountAsync(TaskListFilter filter, CancellationToken cancellationToken)
    {
        // Счётчики показывают, сколько задач найдётся в каждом статусе, поэтому сам фильтр статуса здесь снят.
        var query = Filtered(filter with { StatusId = null, StatusType = null });

        // Matched — число задач с учётом всех фильтров: по нему таблица считает количество страниц.
        var matched = await Filtered(filter).CountAsync(cancellationToken);

        // Группируем по статусу задачи, а тип подтягиваем к нему же: один проход вместо двух GROUP BY.
        var byStatus = await query
            .GroupBy(t => t.StatusId)
            .Select(g => new
            {
                StatusId = g.Key,
                Count = g.Count(),
                Type = db.Statuses.Where(s => s.Id == g.Key).Select(s => s.Type).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var byType = byStatus
            .GroupBy(x => x.Type)
            .Select(g => (g.Key, Count: g.Sum(x => x.Count)))
            .ToList();

        return new TaskCounts(
            byStatus.Sum(x => x.Count),
            matched,
            byType,
            byStatus.Select(x => (x.StatusId, x.Count)).ToList());
    }

    private IQueryable<TaskItem> Filtered(TaskListFilter filter)
    {
        var query = db.TaskItems.AsQueryable();

        if (filter.BoardId is { } boardId)
            query = query.Where(t => t.BoardId == boardId);

        if (filter.AssigneeId is { } assigneeId)
            query = query.Where(t => t.AssigneeId == assigneeId);
        else if (filter.Unassigned)
            query = query.Where(t => t.AssigneeId == null);

        if (filter.StatusId is { } statusId)
            query = query.Where(t => t.StatusId == statusId);

        if (filter.StatusType is { } statusType)
            query = query.Where(t => db.Statuses.Any(s => s.Id == t.StatusId && s.Type == statusType));

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            // Тот же поиск, что раньше делал клиент по загруженному списку: по названию и по коду задачи.
            // Code — value object с конверсией в text, и ILIKE по нему не собирается: из-за конвертера EF пытается
            // привести строку-шаблон к TaskCode. Поэтому совпадения по коду выбирает подзапрос на сыром SQL —
            // он остаётся частью того же оператора, задачи в память не выгружаются.
            var pattern = $"%{Escape(filter.Query.Trim())}%";
            // Колонка называется Value: SqlQuery<T> скалярного типа читает результат именно из неё.
            var matchedByCode = db.Database.SqlQuery<Guid>(
                $"""SELECT "Id" AS "Value" FROM "TaskItems" WHERE "Code" ILIKE {pattern}""");

            query = query.Where(t => EF.Functions.ILike(t.Title, pattern) || matchedByCode.Contains(t.Id));
        }

        return query;
    }

    // ILIKE трактует % и _ как шаблон: в пользовательском запросе они должны искаться буквально.
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<IReadOnlyDictionary<Guid, int>> CountByBoardIdsAsync(
        IReadOnlyCollection<Guid> boardIds, CancellationToken cancellationToken)
    {
        if (boardIds.Count == 0)
            return new Dictionary<Guid, int>();

        return await db.TaskItems
            .Where(t => boardIds.Contains(t.BoardId))
            .GroupBy(t => t.BoardId)
            .Select(g => new { BoardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BoardId, x => x.Count, cancellationToken);
    }

    public Task<bool> StatusBelongsToBoardAsync(Guid statusId, Guid boardId, CancellationToken cancellationToken) =>
        db.Statuses.AnyAsync(s => s.Id == statusId && s.BoardId == boardId, cancellationToken);

    /// <summary>
    /// Код — value object с конвертером, поэтому сравнивается целиком: обращение к .Value внутри
    /// выражения EF не переводит. Коды в базе всегда в верхнем регистре (ключ доски такой), запрос
    /// приводится к нему здесь.
    /// </summary>
    public Task<TaskItem?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Task.FromResult<TaskItem?>(null);

        var normalized = TaskCode.FromValue(code.Trim().ToUpperInvariant());
        return db.TaskItems.FirstOrDefaultAsync(t => t.Code == normalized, cancellationToken);
    }

    public void Add(TaskItem task) => db.TaskItems.Add(task);

    public void Remove(TaskItem task) => db.TaskItems.Remove(task);
}
