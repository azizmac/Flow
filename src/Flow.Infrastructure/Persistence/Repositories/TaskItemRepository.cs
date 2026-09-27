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

    public async Task<IReadOnlyList<TaskItem>> GetByStatusIdAsync(Guid statusId, CancellationToken cancellationToken) =>
        await db.TaskItems.Where(t => t.StatusId == statusId).ToListAsync(cancellationToken);

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
        // ORDER BY из FQL — список ключей; без него — одна колонка таблицы.
        var orders = filter.Orders is { Count: > 0 } fromFql ? fromFql : [new TaskOrder(filter.Sort, filter.Descending)];

        IOrderedQueryable<TaskItem>? ordered = null;
        void By<TKey>(Expression<Func<TaskItem, TKey>> key, bool desc) =>
            ordered = ordered is null
                ? desc ? query.OrderByDescending(key) : query.OrderBy(key)
                : desc ? ordered.ThenByDescending(key) : ordered.ThenBy(key);

        foreach (var (field, desc) in orders)
        {
            switch (field)
            {
                // Внутри проекта разворачиваем и номера: при обратной сортировке ожидается WEB-9, WEB-8, …
                case TaskSortField.Code:
                    By(t => db.Boards.Where(b => b.Id == t.BoardId).Select(b => b.Key).FirstOrDefault(), desc);
                    By(t => t.CreatedAt, desc);
                    break;
                case TaskSortField.Title:
                    By(t => t.Title, desc);
                    break;
                case TaskSortField.Status:
                    By(t => db.Statuses.Where(s => s.Id == t.StatusId).Select(s => s.SortOrder).FirstOrDefault(), desc);
                    break;
                // Без исполнителя — в конец при любом направлении: пустые строки иначе всплывали бы наверх.
                case TaskSortField.Assignee:
                    By(t => t.AssigneeId == null, false);
                    By(t => db.Users.Where(u => u.Id == t.AssigneeId).Select(u => u.LastName + " " + u.FirstName).FirstOrDefault(), desc);
                    break;
                case TaskSortField.Due:
                    By(t => t.DueDate == null, false);
                    By(t => t.DueDate, desc);
                    break;
                case TaskSortField.Priority:
                    By(t => t.Priority, desc);
                    break;
                case TaskSortField.Updated:
                    By(t => t.UpdatedAt, desc);
                    break;
                // Ранг уникален только внутри проекта; в сводном списке порядок проектов задаёт ключ.
                case TaskSortField.Rank:
                    By(t => t.BoardId, desc);
                    By(t => t.Rank, desc);
                    break;
                default:
                    By(t => t.CreatedAt, desc);
                    break;
            }
        }

        return ordered!.ThenBy(t => t.Id);
    }

    public async Task<TaskCounts> CountAsync(TaskListFilter filter, CancellationToken cancellationToken)
    {
        // Счётчики показывают, сколько задач найдётся в каждом статусе, поэтому сам фильтр статуса здесь снят.
        var query = Filtered(filter with { StatusId = null, StatusType = null, UntypedStatus = false });

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

    public async Task<IReadOnlyList<TaskItem>> GetBySprintIdAsync(Guid sprintId, CancellationToken cancellationToken) =>
        await db.TaskItems.Where(t => t.SprintId == sprintId).OrderBy(t => t.Rank).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> GetByMilestoneIdAsync(Guid milestoneId, CancellationToken cancellationToken) =>
        await db.TaskItems.Where(t => t.MilestoneId == milestoneId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, MilestoneCounts>> CountByMilestonesAsync(
        IReadOnlyCollection<Guid> milestoneIds, DateOnly today, DateTime closedSince, CancellationToken cancellationToken)
    {
        if (milestoneIds.Count == 0)
            return new Dictionary<Guid, MilestoneCounts>();

        // Один GROUP BY с join статусов: финальность и вид — признаки статуса, не задачи.
        var rows = await db.TaskItems
            .Where(t => t.MilestoneId != null && milestoneIds.Contains(t.MilestoneId.Value))
            .Join(db.Statuses, t => t.StatusId, s => s.Id, (t, s) => new
            {
                MilestoneId = t.MilestoneId!.Value,
                s.IsFinal,
                Working = s.Type == StatusType.InProgress || s.Type == StatusType.InReview,
                Points = t.StoryPoints ?? 0,
                t.DueDate,
                t.StatusChangedAt
            })
            .GroupBy(x => x.MilestoneId)
            .Select(g => new
            {
                MilestoneId = g.Key,
                Total = g.Count(),
                Done = g.Count(x => x.IsFinal),
                InProgress = g.Count(x => !x.IsFinal && x.Working),
                Points = g.Sum(x => x.Points),
                DonePoints = g.Sum(x => x.IsFinal ? x.Points : 0),
                Overdue = g.Count(x => !x.IsFinal && x.DueDate != null && x.DueDate < today),
                ClosedRecently = g.Count(x => x.IsFinal && x.StatusChangedAt >= closedSince)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.MilestoneId,
            x => new MilestoneCounts(x.Total, x.Done, x.InProgress, x.Points, x.DonePoints, x.Overdue, x.ClosedRecently));
    }

    public async Task<IReadOnlyList<Guid>> MatchingIdsAsync(TaskListFilter filter, CancellationToken cancellationToken) =>
        await Filtered(filter).Select(t => t.Id).ToListAsync(cancellationToken);

    private IQueryable<TaskItem> Filtered(TaskListFilter filter)
    {
        var query = db.TaskItems.AsQueryable();

        if (filter.BoardId is { } boardId)
            query = query.Where(t => t.BoardId == boardId);

        if (filter.VisibleBoardIds is { } visible)
            query = query.Where(t => visible.Contains(t.BoardId));

        if (filter.AssigneeId is { } assigneeId)
            query = query.Where(t => t.AssigneeId == assigneeId);
        else if (filter.Unassigned)
            query = query.Where(t => t.AssigneeId == null);

        if (filter.StatusId is { } statusId)
            query = query.Where(t => t.StatusId == statusId);

        if (filter.StatusType is { } statusType)
            query = query.Where(t => db.Statuses.Any(s => s.Id == t.StatusId && s.Type == statusType));

        if (filter.UntypedStatus)
            query = query.Where(t => db.Statuses.Any(s => s.Id == t.StatusId && s.Type == null));

        // Окно финальной колонки канбана: у каждого проекта своё число дней, поэтому оно берётся из доски задачи.
        if (filter.DoneWindowAt is { } now)
            query = query.Where(t =>
                !db.Statuses.Any(s => s.Id == t.StatusId && s.IsFinal)
                || t.StatusChangedAt >= now.AddDays(-db.Boards.Where(b => b.Id == t.BoardId).Select(b => b.DoneColumnDays).First()));

        // Вид типа — общий ключ для всех проектов, как StatusType: «все ошибки» ищутся без знания Id типов.
        if (filter.TypeKind is { } typeKind)
            query = query.Where(t => db.TaskTypes.Any(tt => tt.Id == t.TypeId && tt.Kind == typeKind));

        if (filter.Priority is { } priority)
            query = query.Where(t => t.Priority == priority);

        if (filter.ParentId is { } parentId)
            query = query.Where(t => t.ParentId == parentId);

        // Условие FQL (docs/TZ_task_views.md §7) — тем же Where, поэтому и страница, и счётчики считаются с ним.
        if (filter.Condition is { } condition)
            query = query.Where(TaskFilterTranslator.ToPredicate(condition, db));

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
    internal static string EscapeLike(string value) => Escape(value);

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

    public async Task<IReadOnlyList<TaskItem>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken) =>
        await db.TaskItems.Where(t => t.ParentId == parentId).OrderBy(t => t.Rank).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ChildCounts>> CountChildrenAsync(IReadOnlyCollection<Guid> parentIds, CancellationToken cancellationToken)
    {
        if (parentIds.Count == 0)
            return new Dictionary<Guid, ChildCounts>();

        // Финальность — признак статуса, поэтому join до группировки: одна строка на родителя, не N+1.
        var rows = await db.TaskItems
            .Where(t => t.ParentId != null && parentIds.Contains(t.ParentId.Value))
            .Join(db.Statuses, t => t.StatusId, s => s.Id, (t, s) => new { ParentId = t.ParentId!.Value, s.IsFinal })
            .GroupBy(x => x.ParentId)
            .Select(g => new { ParentId = g.Key, Total = g.Count(), Done = g.Count(x => x.IsFinal) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.ParentId, x => new ChildCounts(x.Total, x.Done));
    }

    public async Task<IReadOnlyList<TaskTreeEntry>> GetTreeAsync(Guid boardId, Guid? rootId, CancellationToken cancellationToken)
    {
        // Рекурсивный CTE с путём из рангов: сортировка по пути даёт порядок обхода «родитель, затем его
        // поддерево», а внутри уровня — ручной порядок. Разделитель '/' меньше любой цифры base-62, поэтому
        // «a0/…» (дети a0) встаёт между «a0» и «a0V». COLLATE "C" — чтобы сравнение шло по байтам, как у Rank.
        var rows = rootId is { } root
            ? await db.Database.SqlQuery<TreeRow>($"""
                WITH RECURSIVE tree AS (
                    SELECT t."Id", 0 AS "Depth", (t."Rank" || '/') COLLATE "C" AS "Path"
                    FROM "TaskItems" t WHERE t."BoardId" = {boardId} AND t."Id" = {root}
                    UNION ALL
                    SELECT c."Id", tree."Depth" + 1, (tree."Path" || c."Rank" || '/') COLLATE "C"
                    FROM "TaskItems" c JOIN tree ON c."ParentId" = tree."Id")
                SELECT "Id", "Depth", "Path" FROM tree
                """).OrderBy(r => r.Path).ToListAsync(cancellationToken)
            : await db.Database.SqlQuery<TreeRow>($"""
                WITH RECURSIVE tree AS (
                    SELECT t."Id", 0 AS "Depth", (t."Rank" || '/') COLLATE "C" AS "Path"
                    FROM "TaskItems" t WHERE t."BoardId" = {boardId} AND t."ParentId" IS NULL
                    UNION ALL
                    SELECT c."Id", tree."Depth" + 1, (tree."Path" || c."Rank" || '/') COLLATE "C"
                    FROM "TaskItems" c JOIN tree ON c."ParentId" = tree."Id")
                SELECT "Id", "Depth", "Path" FROM tree
                """).OrderBy(r => r.Path).ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var items = await db.TaskItems.Where(t => ids.Contains(t.Id)).AsNoTracking().ToDictionaryAsync(t => t.Id, cancellationToken);

        return rows.Where(r => items.ContainsKey(r.Id)).Select(r => new TaskTreeEntry(items[r.Id], r.Depth)).ToList();
    }

    /// <summary>Строка рекурсивного CTE дерева; SqlQuery читает колонки по именам свойств.</summary>
    private sealed class TreeRow
    {
        public Guid Id { get; init; }
        public int Depth { get; init; }
        public string Path { get; init; } = "";
    }

    public Task<string?> GetMaxRankAsync(Guid boardId, Guid? excludeTaskId, CancellationToken cancellationToken) =>
        db.TaskItems
            .Where(t => t.BoardId == boardId && t.Id != excludeTaskId)
            .OrderByDescending(t => t.Rank)
            .Select(t => t.Rank)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<string?> GetNeighborRankAsync(Guid boardId, string rank, bool after, Guid excludeTaskId, CancellationToken cancellationToken)
    {
        // Параметр сравнивается с колонкой и получает её COLLATE "C": порядок тот же, что в индексе (BoardId, Rank).
        var query = db.TaskItems.Where(t => t.BoardId == boardId && t.Id != excludeTaskId);

        return after
            ? query.Where(t => string.Compare(t.Rank, rank) > 0).OrderBy(t => t.Rank).Select(t => t.Rank).FirstOrDefaultAsync(cancellationToken)
            : query.Where(t => string.Compare(t.Rank, rank) < 0).OrderByDescending(t => t.Rank).Select(t => t.Rank).FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(TaskItem task) => db.TaskItems.Add(task);

    public void Remove(TaskItem task) => db.TaskItems.Remove(task);
}
