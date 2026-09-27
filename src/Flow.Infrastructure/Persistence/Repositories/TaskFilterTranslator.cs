using System.Linq.Expressions;
using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="TaskFilterNode"/> → предикат для <c>Where</c> (docs/TZ_task_views.md §7). Узлы собираются
/// выражениями, а не строкой SQL: EF сам параметризует значения и кладёт подзапросы (виды статусов и типов,
/// связи) в тот же оператор. Значения идут через замыкание (<see cref="Box{T}"/>), а не константами — иначе
/// каждое новое значение давало бы новый план запроса. Отрицание — C#-семантика null (EF её сохраняет):
/// «assignee != X» включает задачи без исполнителя.
/// </summary>
internal static class TaskFilterTranslator
{
    public static Expression<Func<TaskItem, bool>> ToPredicate(TaskFilterNode node, FlowDbContext db) => node switch
    {
        TaskFilterAnd and => Combine(and.Items.Select(i => ToPredicate(i, db)), Expression.AndAlso, true),
        TaskFilterOr or => Combine(or.Items.Select(i => ToPredicate(i, db)), Expression.OrElse, false),
        TaskFilterNot not => Not(ToPredicate(not.Item, db)),
        TaskFilterIn @in => In(@in),
        TaskFilterIsEmpty empty => Empty(empty.Field, db),
        TaskFilterTypeKinds kinds => TypeKinds(kinds.Kinds.ToList(), db),
        TaskFilterStatusTypes types => StatusTypes(types.Types.Select(t => (StatusType?)t).ToList(), db),
        TaskFilterCompare compare => Compare(compare),
        TaskFilterText text => Text(text.Text, db),
        TaskFilterBlocked => Blocked(db),
        _ => throw new NotSupportedException($"Filter node {node.GetType().Name} is not supported.")
    };

    private static Expression<Func<TaskItem, bool>> In(TaskFilterIn node)
    {
        var ids = node.Ids.ToList();
        var nullable = node.Ids.Select(id => (Guid?)id).ToList();
        return node.Field switch
        {
            TaskFilterRef.Id => t => ids.Contains(t.Id),
            TaskFilterRef.Board => t => ids.Contains(t.BoardId),
            TaskFilterRef.Type => t => ids.Contains(t.TypeId),
            TaskFilterRef.Status => t => ids.Contains(t.StatusId),
            TaskFilterRef.Assignee => t => nullable.Contains(t.AssigneeId),
            TaskFilterRef.Creator => t => nullable.Contains(t.CreatedById),
            TaskFilterRef.Parent => t => nullable.Contains(t.ParentId),
            _ => throw new NotSupportedException($"Field {node.Field} is not supported.")
        };
    }

    private static Expression<Func<TaskItem, bool>> Empty(TaskFilterNullable field, FlowDbContext db) => field switch
    {
        TaskFilterNullable.Assignee => t => t.AssigneeId == null,
        TaskFilterNullable.Parent => t => t.ParentId == null,
        TaskFilterNullable.StartDate => t => t.StartDate == null,
        TaskFilterNullable.DueDate => t => t.DueDate == null,
        TaskFilterNullable.StoryPoints => t => t.StoryPoints == null,
        TaskFilterNullable.Estimate => t => t.EstimateMinutes == null,
        TaskFilterNullable.Links => t => !db.TaskLinks.Any(l => l.SourceTaskId == t.Id || l.TargetTaskId == t.Id),
        _ => throw new NotSupportedException($"Field {field} is not supported.")
    };

    private static Expression<Func<TaskItem, bool>> TypeKinds(List<TaskTypeKind> kinds, FlowDbContext db) =>
        t => db.TaskTypes.Any(tt => tt.Id == t.TypeId && kinds.Contains(tt.Kind));

    private static Expression<Func<TaskItem, bool>> StatusTypes(List<StatusType?> types, FlowDbContext db) =>
        t => db.Statuses.Any(s => s.Id == t.StatusId && types.Contains(s.Type));

    private static Expression<Func<TaskItem, bool>> Compare(TaskFilterCompare node) => node.Field switch
    {
        TaskFilterScalar.Priority => Cmp<int>(t => (int)t.Priority, node.Op, node.Value),
        TaskFilterScalar.Created => Cmp<DateTime>(t => t.CreatedAt, node.Op, node.Value),
        TaskFilterScalar.Updated => Cmp<DateTime>(t => t.UpdatedAt, node.Op, node.Value),
        TaskFilterScalar.StartDate => Cmp<DateOnly?>(t => t.StartDate, node.Op, node.Value),
        TaskFilterScalar.DueDate => Cmp<DateOnly?>(t => t.DueDate, node.Op, node.Value),
        TaskFilterScalar.StoryPoints => Cmp<decimal?>(t => t.StoryPoints, node.Op, node.Value),
        TaskFilterScalar.Estimate => Cmp<int?>(t => t.EstimateMinutes, node.Op, node.Value),
        _ => throw new NotSupportedException($"Field {node.Field} is not supported.")
    };

    private static Expression<Func<TaskItem, bool>> Cmp<T>(Expression<Func<TaskItem, T>> selector, TaskFilterOp op, object value)
    {
        var holder = Expression.Property(Expression.Constant(new Box<T>((T)value)), nameof(Box<T>.Value));
        Expression body = op switch
        {
            TaskFilterOp.Eq => Expression.Equal(selector.Body, holder),
            TaskFilterOp.Gt => Expression.GreaterThan(selector.Body, holder),
            TaskFilterOp.Gte => Expression.GreaterThanOrEqual(selector.Body, holder),
            TaskFilterOp.Lt => Expression.LessThan(selector.Body, holder),
            _ => Expression.LessThanOrEqual(selector.Body, holder)
        };
        return Expression.Lambda<Func<TaskItem, bool>>(body, selector.Parameters);
    }

    /// <summary>Как подстрока в списке задач: название, описание и код; код — сырым подзапросом из-за конвертера TaskCode.</summary>
    private static Expression<Func<TaskItem, bool>> Text(string text, FlowDbContext db)
    {
        var pattern = $"%{TaskItemRepository.EscapeLike(text)}%";
        var matchedByCode = db.Database.SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "TaskItems" WHERE "Code" ILIKE {pattern}""");
        return t => EF.Functions.ILike(t.Title, pattern)
                    || (t.Description != null && EF.Functions.ILike(t.Description, pattern))
                    || matchedByCode.Contains(t.Id);
    }

    /// <summary>Та же логика, что у TaskResponse.BlockedByCount: входящий Blocks от задачи не в финальном статусе.</summary>
    private static Expression<Func<TaskItem, bool>> Blocked(FlowDbContext db) =>
        t => db.TaskLinks.Any(l => l.Type == TaskLinkType.Blocks && l.TargetTaskId == t.Id
                                   && db.TaskItems.Any(s => s.Id == l.SourceTaskId && db.Statuses.Any(st => st.Id == s.StatusId && !st.IsFinal)));

    private static Expression<Func<TaskItem, bool>> Not(Expression<Func<TaskItem, bool>> inner) =>
        Expression.Lambda<Func<TaskItem, bool>>(Expression.Not(inner.Body), inner.Parameters);

    private static Expression<Func<TaskItem, bool>> Combine(
        IEnumerable<Expression<Func<TaskItem, bool>>> items, Func<Expression, Expression, BinaryExpression> join, bool emptyValue)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return emptyValue ? _ => true : _ => false;

        var parameter = list[0].Parameters[0];
        var body = list[0].Body;
        foreach (var next in list.Skip(1))
            body = join(body, new ReplaceParameter(next.Parameters[0], parameter).Visit(next.Body));

        return Expression.Lambda<Func<TaskItem, bool>>(body, parameter);
    }

    private sealed class Box<T>(T value)
    {
        public T Value { get; } = value;
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
