using Flow.Application.Abstractions;
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

        // Keyset вместо OFFSET: страницы не разъезжаются, когда во время листания добавляют задачу.
        if (filter.BeforeCreatedAt is { } at && filter.BeforeId is { } id)
            query = query.Where(t => t.CreatedAt < at || (t.CreatedAt == at && t.Id.CompareTo(id) < 0));

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(filter.Limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskCounts> CountAsync(TaskListFilter filter, CancellationToken cancellationToken)
    {
        // Счётчики показывают, сколько задач найдётся в каждом статусе, поэтому сам фильтр статуса здесь снят.
        var query = Filtered(filter with { StatusId = null, StatusType = null });

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

    public void Add(TaskItem task) => db.TaskItems.Add(task);

    public void Remove(TaskItem task) => db.TaskItems.Remove(task);
}
