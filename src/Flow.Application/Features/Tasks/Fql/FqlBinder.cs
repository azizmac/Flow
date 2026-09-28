using System.Globalization;
using System.Text.RegularExpressions;
using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Features.Tasks.Fql;

/// <summary>Результат биндинга: условие (null — без условий) и порядок из ORDER BY (пусто — порядок вызывающего).</summary>
public sealed record FqlBound(TaskFilterNode? Filter, IReadOnlyList<TaskOrder> Orders);

/// <summary>
/// Имена из FQL — в Id и значения <see cref="TaskFilterNode"/> (docs/TZ_task_views.md §7). Проекты — только видимые:
/// ключ скрытого проекта отвечает «не найден», как и несуществующий. Имя статуса или типа ищется во всех видимых
/// проектах: «status = Сделана» в режиме «все проекты» — это все статусы с таким именем. Неизвестное имя — ошибка
/// с местом в строке, а не пустая выдача: пустой результат без объяснения хуже ошибки.
/// Относительные даты считаются от <paramref name="today"/> (UTC-дата сервера).
/// </summary>
public sealed partial class FqlBinder(IFqlLookup lookup, Guid actorId, DateOnly today)
{
    private IReadOnlyList<Board> _boards = [];

    public static async Task<FqlBound> BindAsync(string? query, IFqlLookup lookup, Guid actorId, DateOnly today, CancellationToken cancellationToken)
    {
        var parsed = FqlParser.Parse(query);
        var binder = new FqlBinder(lookup, actorId, today);
        binder._boards = await lookup.VisibleBoardsAsync(cancellationToken);
        var filter = parsed.Where is null ? null : await binder.BindAsync(parsed.Where, cancellationToken);
        return new FqlBound(filter, parsed.Orders.Select(BindOrder).ToList());
    }

    private static TaskOrder BindOrder(FqlOrder order) =>
        FqlFields.Orders.TryGetValue(order.Field.Text, out var field)
            ? new TaskOrder(field, order.Descending)
            : throw new FqlException($"По полю «{order.Field.Text}» сортировать нельзя; можно: {string.Join(", ", FqlFields.Orders.Keys)}",
                order.Field.Position, order.Field.Length);

    private async Task<TaskFilterNode> BindAsync(FqlExpr expr, CancellationToken ct) => expr switch
    {
        FqlAnd and => new TaskFilterAnd(await BindAllAsync(and.Items, ct)),
        FqlOr or => new TaskFilterOr(await BindAllAsync(or.Items, ct)),
        FqlNot not => new TaskFilterNot(await BindAsync(not.Item, ct)),
        FqlClause clause => await ClauseAsync(clause, ct),
        _ => throw new InvalidOperationException($"Unknown FQL node {expr.GetType().Name}.")
    };

    private async Task<IReadOnlyList<TaskFilterNode>> BindAllAsync(IReadOnlyList<FqlExpr> items, CancellationToken ct)
    {
        var result = new List<TaskFilterNode>(items.Count);
        foreach (var item in items)
            result.Add(await BindAsync(item, ct));
        return result;
    }

    private async Task<TaskFilterNode> ClauseAsync(FqlClause c, CancellationToken ct)
    {
        var name = c.Field.Text;
        if (name.StartsWith("cf.", StringComparison.OrdinalIgnoreCase))
            return await CustomFieldAsync(c, name[3..], ct);
        if (FqlFields.Future.TryGetValue(name, out var later))
            throw Error($"Поля «{name}» пока нет: {later}", c.Field);

        return name.ToLowerInvariant() switch
        {
            "project" => Refs(c, TaskFilterRef.Board, v => [ProjectId(v)]),
            "type" => Refs(c, TaskFilterRef.Type, TypeIds),
            "status" => Refs(c, TaskFilterRef.Status, StatusIds),
            "key" => Refs(c, TaskFilterRef.Id, await TaskIdsAsync(c.Values, ct)),
            "assignee" => await PeopleAsync(c, TaskFilterRef.Assignee, TaskFilterNullable.Assignee, ct),
            "creator" => await PeopleAsync(c, TaskFilterRef.Creator, null, ct),
            "parent" => await ParentAsync(c, ct),
            "typekind" => Enums(c, v => Lookup(FqlFields.Kinds, v, "вид задачи"), kinds => new TaskFilterTypeKinds(kinds)),
            "statuscategory" => Enums(c, v => Lookup(FqlFields.StatusTypes, v, "вид статуса"), types => new TaskFilterStatusTypes(types)),
            "priority" => Scalar(c, TaskFilterScalar.Priority, null, v => (int)Lookup(FqlFields.Priorities, v, "приоритет")),
            "created" => Dates(c, TaskFilterScalar.Created),
            "updated" => Dates(c, TaskFilterScalar.Updated),
            "start" => Dates(c, TaskFilterScalar.StartDate),
            "due" => Dates(c, TaskFilterScalar.DueDate),
            "points" => Scalar(c, TaskFilterScalar.StoryPoints, TaskFilterNullable.StoryPoints, Points),
            "estimate" => Scalar(c, TaskFilterScalar.Estimate, TaskFilterNullable.Estimate, v => Duration(v)),
            "text" => Text(c),
            "linked" => await LinkedAsync(c, ct),
            "sprint" => await SprintAsync(c, ct),
            "milestone" => await MilestoneAsync(c, ct),
            "development" => Enums(c, v => Lookup(FqlFields.Development, v, "признак разработки"),
                states => states.Count == 1 ? new TaskFilterPullRequest(states[0]) : new TaskFilterOr(states.Select(s => (TaskFilterNode)new TaskFilterPullRequest(s)).ToList())),
            _ => throw Error($"Неизвестное поле «{name}». Поля: {string.Join(", ", FqlFields.All.Select(f => f.Name))}", c.Field)
        };
    }

    // ---- поля-ссылки: =, !=, IN, NOT IN ----

    private static TaskFilterNode Refs(FqlClause c, TaskFilterRef field, Func<FqlValue, IEnumerable<Guid>> resolve) =>
        Refs(c, field, c.Values.SelectMany(resolve).Distinct().ToList());

    private static TaskFilterNode Refs(FqlClause c, TaskFilterRef field, IReadOnlyList<Guid> ids)
    {
        EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.In, FqlOperator.NotIn);
        TaskFilterNode node = new TaskFilterIn(field, ids);
        return IsNegative(c) ? new TaskFilterNot(node) : node;
    }

    private Guid ProjectId(FqlValue v) =>
        _boards.FirstOrDefault(b => string.Equals(b.Key, v.Text, StringComparison.OrdinalIgnoreCase))?.Id
        ?? throw Error($"Проект «{v.Text}» не найден", v);

    private IEnumerable<Guid> StatusIds(FqlValue v)
    {
        var ids = _boards.SelectMany(b => b.Statuses).Where(s => string.Equals(s.Name, v.Text, StringComparison.OrdinalIgnoreCase)).Select(s => s.Id).ToList();
        return ids.Count > 0 ? ids : throw Error($"Статуса «{v.Text}» нет ни в одном проекте", v);
    }

    private IEnumerable<Guid> TypeIds(FqlValue v)
    {
        var ids = _boards.SelectMany(b => b.TaskTypes).Where(t => string.Equals(t.Name, v.Text, StringComparison.OrdinalIgnoreCase)).Select(t => t.Id).ToList();
        return ids.Count > 0 ? ids : throw Error($"Типа «{v.Text}» нет ни в одном проекте", v);
    }

    private async Task<IReadOnlyList<Guid>> TaskIdsAsync(IReadOnlyList<FqlValue> values, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var v in values)
            ids.Add((await TaskAsync(v, ct)).Id);
        return ids;
    }

    private async Task<TaskItem> TaskAsync(FqlValue v, CancellationToken ct) =>
        await lookup.TaskByCodeAsync(v.Text, ct) ?? throw Error($"Задача «{v.Text}» не найдена", v);

    private async Task<TaskFilterNode> PeopleAsync(FqlClause c, TaskFilterRef field, TaskFilterNullable? nullable, CancellationToken ct)
    {
        if (c.Operator is FqlOperator.IsEmpty or FqlOperator.IsNotEmpty)
            return Empty(c, nullable ?? throw Error($"У поля «{c.Field.Text}» пустых значений не бывает", c.Field));

        EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.In, FqlOperator.NotIn);

        var ids = new List<Guid>();
        var orEmpty = false;
        var usernames = new List<(FqlValue Value, string Name)>();
        foreach (var v in c.Values)
        {
            if (v.IsFunction("me"))
                ids.Add(actorId);
            else if (!v.Quoted && v.Text.Equals("EMPTY", StringComparison.OrdinalIgnoreCase) && nullable is not null)
                orEmpty = true;
            else if (v.Function is not null)
                throw Error($"Функции {v.Function}() у поля «{c.Field.Text}» нет; есть me()", v);
            else
                usernames.Add((v, v.Text.TrimStart('@').ToLowerInvariant()));
        }

        if (usernames.Count > 0)
        {
            var found = await lookup.UsersByUsernameAsync(usernames.Select(u => u.Name).ToList(), ct);
            foreach (var (value, username) in usernames)
                ids.Add(found.TryGetValue(username, out var id) ? id : throw Error($"Пользователь «{value.Text}» не найден", value));
        }

        TaskFilterNode node = new TaskFilterIn(field, ids);
        if (orEmpty)
            node = new TaskFilterOr([node, new TaskFilterIsEmpty(nullable!.Value)]);
        return IsNegative(c) ? new TaskFilterNot(node) : node;
    }

    /// <summary>parent = КОД — прямые дети; parent = childrenOf(КОД) — всё поддерево; parent IS EMPTY — верхний уровень.</summary>
    private async Task<TaskFilterNode> ParentAsync(FqlClause c, CancellationToken ct)
    {
        if (c.Operator is FqlOperator.IsEmpty or FqlOperator.IsNotEmpty)
            return Empty(c, TaskFilterNullable.Parent);

        EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.In, FqlOperator.NotIn);

        var parents = new List<Guid>();
        var subtree = new List<Guid>();
        foreach (var v in c.Values)
        {
            if (v.IsFunction("childrenOf"))
                subtree.AddRange(await lookup.DescendantsAsync(await TaskAsync(SingleArg(v), ct), ct));
            else if (v.Function is not null)
                throw Error($"Функции {v.Function}() у поля «parent» нет; есть childrenOf(КОД)", v);
            else
                parents.Add((await TaskAsync(v, ct)).Id);
        }

        TaskFilterNode node = (parents.Count, subtree.Count) switch
        {
            (_, 0) => new TaskFilterIn(TaskFilterRef.Parent, parents),
            (0, _) => new TaskFilterIn(TaskFilterRef.Id, subtree),
            _ => new TaskFilterOr([new TaskFilterIn(TaskFilterRef.Parent, parents), new TaskFilterIn(TaskFilterRef.Id, subtree)])
        };
        return IsNegative(c) ? new TaskFilterNot(node) : node;
    }

    /// <summary>
    /// sprint = "Спринт 3" (по имени во всех видимых проектах) | openSprints() (запланированные и активный) |
    /// closedSprints() (завершённые); sprint IS EMPTY — бэклог, как и значение EMPTY в списке.
    /// </summary>
    private async Task<TaskFilterNode> SprintAsync(FqlClause c, CancellationToken ct)
    {
        if (c.Operator is FqlOperator.IsEmpty or FqlOperator.IsNotEmpty)
            return Empty(c, TaskFilterNullable.Sprint);

        EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.In, FqlOperator.NotIn);

        var sprints = await lookup.SprintsAsync(ct);
        var ids = new List<Guid>();
        var orEmpty = false;
        foreach (var v in c.Values)
        {
            if (v.IsFunction("openSprints"))
                ids.AddRange(sprints.Where(s => !s.IsCompleted).Select(s => s.Id));
            else if (v.IsFunction("closedSprints"))
                ids.AddRange(sprints.Where(s => s.IsCompleted).Select(s => s.Id));
            else if (v.Function is not null)
                throw Error($"Функции {v.Function}() у поля «sprint» нет; есть openSprints(), closedSprints()", v);
            else if (!v.Quoted && v.Text.Equals("EMPTY", StringComparison.OrdinalIgnoreCase))
                orEmpty = true;
            else
            {
                var named = sprints.Where(s => string.Equals(s.Name, v.Text, StringComparison.OrdinalIgnoreCase)).Select(s => s.Id).ToList();
                ids.AddRange(named.Count > 0 ? named : throw Error($"Спринта «{v.Text}» нет ни в одном проекте", v));
            }
        }

        TaskFilterNode node = new TaskFilterIn(TaskFilterRef.Sprint, ids.Distinct().ToList());
        if (orEmpty)
            node = new TaskFilterOr([node, new TaskFilterIsEmpty(TaskFilterNullable.Sprint)]);
        return IsNegative(c) ? new TaskFilterNot(node) : node;
    }

    /// <summary>
    /// milestone = "1.0" (по имени во всех видимых проектах) | openMilestones() | closedMilestones();
    /// milestone IS EMPTY — без вехи, как и значение EMPTY в списке.
    /// </summary>
    private async Task<TaskFilterNode> MilestoneAsync(FqlClause c, CancellationToken ct)
    {
        if (c.Operator is FqlOperator.IsEmpty or FqlOperator.IsNotEmpty)
            return Empty(c, TaskFilterNullable.Milestone);

        EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.In, FqlOperator.NotIn);

        var milestones = await lookup.MilestonesAsync(ct);
        var ids = new List<Guid>();
        var orEmpty = false;
        foreach (var v in c.Values)
        {
            if (v.IsFunction("openMilestones"))
                ids.AddRange(milestones.Where(m => !m.IsClosed).Select(m => m.Id));
            else if (v.IsFunction("closedMilestones"))
                ids.AddRange(milestones.Where(m => m.IsClosed).Select(m => m.Id));
            else if (v.Function is not null)
                throw Error($"Функции {v.Function}() у поля «milestone» нет; есть openMilestones(), closedMilestones()", v);
            else if (!v.Quoted && v.Text.Equals("EMPTY", StringComparison.OrdinalIgnoreCase))
                orEmpty = true;
            else
            {
                var named = milestones.Where(m => string.Equals(m.Name, v.Text, StringComparison.OrdinalIgnoreCase)).Select(m => m.Id).ToList();
                ids.AddRange(named.Count > 0 ? named : throw Error($"Вехи «{v.Text}» нет ни в одном проекте", v));
            }
        }

        TaskFilterNode node = new TaskFilterIn(TaskFilterRef.Milestone, ids.Distinct().ToList());
        if (orEmpty)
            node = new TaskFilterOr([node, new TaskFilterIsEmpty(TaskFilterNullable.Milestone)]);
        return IsNegative(c) ? new TaskFilterNot(node) : node;
    }

    /// <summary>linked = linkedTo(КОД) | blockedBy(КОД) | isBlocked(); linked IS [NOT] EMPTY — есть ли связи вообще.</summary>
    private async Task<TaskFilterNode> LinkedAsync(FqlClause c, CancellationToken ct)
    {
        if (c.Operator is FqlOperator.IsEmpty or FqlOperator.IsNotEmpty)
            return Empty(c, TaskFilterNullable.Links);

        EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.In, FqlOperator.NotIn);

        var nodes = new List<TaskFilterNode>();
        foreach (var v in c.Values)
        {
            if (v.IsFunction("isBlocked"))
                nodes.Add(new TaskFilterBlocked());
            else if (v.IsFunction("linkedTo"))
                nodes.Add(new TaskFilterIn(TaskFilterRef.Id, await lookup.LinkedAsync((await TaskAsync(SingleArg(v), ct)).Id, ct)));
            else if (v.IsFunction("blockedBy"))
                nodes.Add(new TaskFilterIn(TaskFilterRef.Id, await lookup.BlockedByAsync((await TaskAsync(SingleArg(v), ct)).Id, ct)));
            else
                throw Error("У поля «linked» значение — функция: linkedTo(КОД), blockedBy(КОД) или isBlocked()", v);
        }

        TaskFilterNode node = nodes.Count == 1 ? nodes[0] : new TaskFilterOr(nodes);
        return IsNegative(c) ? new TaskFilterNot(node) : node;
    }

    // ---- пользовательские поля cf.<key> (docs/TZ_task_model.md §4) ----

    /// <summary>
    /// Поле ищется по Key во всех видимых проектах (у каждого проекта своё поле с тем же ключом). Операторы — по типу:
    /// текст — = и ~, число и дата — сравнения, списки и люди — = / IN по подписи варианта и @username, флажок —
    /// true/false (незаполненный флажок — это false). Везде IS [NOT] EMPTY.
    /// </summary>
    private async Task<TaskFilterNode> CustomFieldAsync(FqlClause c, string key, CancellationToken ct)
    {
        var fields = _boards.SelectMany(b => b.CustomFields).Where(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (fields.Count == 0)
            throw Error($"Поля «cf.{key}» нет ни в одном проекте", c.Field);
        if (fields.Select(f => f.Type).Distinct().Count() > 1)
            throw Error($"У поля «cf.{key}» в проектах разные типы — уточните условие проектом", c.Field);

        var ids = fields.Select(f => f.Id).ToList();
        var type = fields[0].Type;
        TaskFilterNode Node(TaskFilterCustomOp op, object? value) => new TaskFilterCustomField(ids, type, op, value);

        if (c.Operator is FqlOperator.IsEmpty or FqlOperator.IsNotEmpty)
        {
            var empty = Node(TaskFilterCustomOp.IsEmpty, null);
            return c.Operator == FqlOperator.IsNotEmpty ? new TaskFilterNot(empty) : empty;
        }

        switch (type)
        {
            case CustomFieldType.Text or CustomFieldType.LongText or CustomFieldType.Url:
                if (c.Operator == FqlOperator.Contains)
                    return Node(TaskFilterCustomOp.Contains, c.Values[0].Text);
                return AnyOf(c, v => Node(TaskFilterCustomOp.Eq, v.Text));

            case CustomFieldType.Number:
                return Compared(c, v => decimal.TryParse(v.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                    ? n
                    : throw Error($"«{v.Text}» — не число", v), Node);

            case CustomFieldType.Date:
                return Compared(c, v => Date(v), Node);

            case CustomFieldType.Checkbox:
                return AnyOf(c, v => v.Text.ToLowerInvariant() switch
                {
                    "true" or "да" => Node(TaskFilterCustomOp.Eq, "true"),
                    "false" or "нет" => new TaskFilterOr([Node(TaskFilterCustomOp.Eq, "false"), Node(TaskFilterCustomOp.IsEmpty, null)]),
                    _ => throw Error($"У флажка значение true или false, а не «{v.Text}»", v)
                });

            case CustomFieldType.Select or CustomFieldType.MultiSelect:
            {
                EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.In, FqlOperator.NotIn);
                var options = new List<string>();
                var orEmpty = false;
                foreach (var v in c.Values)
                {
                    if (!v.Quoted && v.Text.Equals("EMPTY", StringComparison.OrdinalIgnoreCase))
                    {
                        orEmpty = true;
                        continue;
                    }

                    var found = fields.SelectMany(f => f.Options).Where(o => string.Equals(o.Label, v.Text, StringComparison.OrdinalIgnoreCase)).ToList();
                    options.AddRange(found.Count > 0 ? found.Select(o => o.Id.ToString()) : throw Error($"У поля «cf.{key}» нет варианта «{v.Text}»", v));
                }
                return Negated(c, WithEmpty(Node(TaskFilterCustomOp.Any, options), orEmpty ? Node(TaskFilterCustomOp.IsEmpty, null) : null));
            }

            case CustomFieldType.User:
            {
                // Люди — тем же разбором, что assignee: me(), @username, EMPTY.
                var people = await PeopleAsync(c with { Operator = c.Operator is FqlOperator.NotEq ? FqlOperator.Eq : c.Operator is FqlOperator.NotIn ? FqlOperator.In : c.Operator },
                    TaskFilterRef.Assignee, TaskFilterNullable.Assignee, ct);
                var (userIds, orEmpty) = people switch
                {
                    TaskFilterIn @in => (@in.Ids, false),
                    TaskFilterOr { Items: [TaskFilterIn @in, TaskFilterIsEmpty] } => (@in.Ids, true),
                    _ => throw Error($"Не удалось разобрать значение поля «cf.{key}»", c.Field)
                };
                return Negated(c, WithEmpty(Node(TaskFilterCustomOp.Any, userIds.Select(id => id.ToString()).ToList()), orEmpty ? Node(TaskFilterCustomOp.IsEmpty, null) : null));
            }
        }

        throw Error($"Поле «cf.{key}» пока не фильтруется", c.Field);
    }

    /// <summary>= / != / IN / NOT IN как «одно из» равенств.</summary>
    private static TaskFilterNode AnyOf(FqlClause c, Func<FqlValue, TaskFilterNode> equal)
    {
        EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.In, FqlOperator.NotIn);
        var nodes = c.Values.Select(equal).ToList();
        return Negated(c, nodes.Count == 1 ? nodes[0] : new TaskFilterOr(nodes));
    }

    /// <summary>Сравнения числа и даты; IN — «одно из» равенств.</summary>
    private static TaskFilterNode Compared(FqlClause c, Func<FqlValue, object> parse, Func<TaskFilterCustomOp, object?, TaskFilterNode> node)
    {
        if (c.Operator is FqlOperator.In or FqlOperator.NotIn or FqlOperator.Eq or FqlOperator.NotEq)
            return AnyOf(c, v => node(TaskFilterCustomOp.Eq, parse(v)));

        EnsureOps(c, FqlOperator.Gt, FqlOperator.Gte, FqlOperator.Lt, FqlOperator.Lte);
        var op = c.Operator switch
        {
            FqlOperator.Gt => TaskFilterCustomOp.Gt,
            FqlOperator.Gte => TaskFilterCustomOp.Gte,
            FqlOperator.Lt => TaskFilterCustomOp.Lt,
            _ => TaskFilterCustomOp.Lte
        };
        return node(op, parse(c.Values[0]));
    }

    private static TaskFilterNode WithEmpty(TaskFilterNode node, TaskFilterNode? empty) =>
        empty is null ? node : new TaskFilterOr([node, empty]);

    private static TaskFilterNode Negated(FqlClause c, TaskFilterNode node) => IsNegative(c) ? new TaskFilterNot(node) : node;

    private static FqlValue SingleArg(FqlValue function) =>
        function.Args is [var arg] ? arg : throw Error($"{function.Function}() принимает один код задачи", function);

    // ---- словари: вид задачи, вид статуса ----

    private static TaskFilterNode Enums<T>(FqlClause c, Func<FqlValue, T> resolve, Func<IReadOnlyList<T>, TaskFilterNode> build)
    {
        EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.In, FqlOperator.NotIn);
        var node = build(c.Values.Select(resolve).Distinct().ToList());
        return IsNegative(c) ? new TaskFilterNot(node) : node;
    }

    private static T Lookup<T>(IReadOnlyDictionary<string, T> map, FqlValue v, string what) =>
        map.TryGetValue(v.Text, out var value)
            ? value
            : throw Error($"Неизвестный {what} «{v.Text}»; можно: {string.Join(", ", map.Keys.Where(k => k.All(char.IsAscii)))}", v);

    // ---- скаляры: приоритет, оценки, даты ----

    private TaskFilterNode Scalar(FqlClause c, TaskFilterScalar field, TaskFilterNullable? nullable, Func<FqlValue, object> parse)
    {
        if (c.Operator is FqlOperator.IsEmpty or FqlOperator.IsNotEmpty)
            return Empty(c, nullable ?? throw Error($"У поля «{c.Field.Text}» пустых значений не бывает", c.Field));

        if (c.Operator is FqlOperator.In or FqlOperator.NotIn)
        {
            TaskFilterNode any = new TaskFilterOr(c.Values.Select(v => (TaskFilterNode)new TaskFilterCompare(field, TaskFilterOp.Eq, parse(v))).ToList());
            return c.Operator == FqlOperator.NotIn ? new TaskFilterNot(any) : any;
        }

        EnsureOps(c, FqlOperator.Eq, FqlOperator.NotEq, FqlOperator.Gt, FqlOperator.Gte, FqlOperator.Lt, FqlOperator.Lte);
        var compare = new TaskFilterCompare(field, Op(c.Operator), parse(c.Values[0]));
        return c.Operator == FqlOperator.NotEq ? new TaskFilterNot(compare) : compare;
    }

    /// <summary>
    /// Даты. У created/updated (момент) «= день» — это интервал суток [день, день+1), «&lt;= день» — до конца дня:
    /// человек пишет дату, а не полночь. У start/due (дата без времени) сравнение прямое.
    /// </summary>
    private TaskFilterNode Dates(FqlClause c, TaskFilterScalar field)
    {
        var isMoment = field is TaskFilterScalar.Created or TaskFilterScalar.Updated;
        if (!isMoment)
            return Scalar(c, field, field == TaskFilterScalar.StartDate ? TaskFilterNullable.StartDate : TaskFilterNullable.DueDate, v => Date(v));

        if (c.Operator is FqlOperator.IsEmpty or FqlOperator.IsNotEmpty)
            throw Error($"У поля «{c.Field.Text}» пустых значений не бывает", c.Field);

        TaskFilterNode Day(FqlValue v)
        {
            var day = Date(v);
            return new TaskFilterAnd([
                new TaskFilterCompare(field, TaskFilterOp.Gte, Start(day)),
                new TaskFilterCompare(field, TaskFilterOp.Lt, Start(day.AddDays(1)))
            ]);
        }

        switch (c.Operator)
        {
            case FqlOperator.In or FqlOperator.NotIn:
                TaskFilterNode any = new TaskFilterOr(c.Values.Select(Day).ToList());
                return c.Operator == FqlOperator.NotIn ? new TaskFilterNot(any) : any;
            case FqlOperator.Eq:
                return Day(c.Values[0]);
            case FqlOperator.NotEq:
                return new TaskFilterNot(Day(c.Values[0]));
        }

        EnsureOps(c, FqlOperator.Gt, FqlOperator.Gte, FqlOperator.Lt, FqlOperator.Lte);
        var date = Date(c.Values[0]);
        return c.Operator switch
        {
            FqlOperator.Gt => new TaskFilterCompare(field, TaskFilterOp.Gte, Start(date.AddDays(1))),
            FqlOperator.Gte => new TaskFilterCompare(field, TaskFilterOp.Gte, Start(date)),
            FqlOperator.Lt => new TaskFilterCompare(field, TaskFilterOp.Lt, Start(date)),
            _ => new TaskFilterCompare(field, TaskFilterOp.Lt, Start(date.AddDays(1)))
        };
    }

    private static DateTime Start(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    [GeneratedRegex(@"^([+-]?)(\d{1,4})([dwmy])$", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeDate();

    private DateOnly Date(FqlValue v)
    {
        if (v.Function is { } fn)
        {
            return fn.ToLowerInvariant() switch
            {
                "today" => today,
                "startofweek" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
                "startofmonth" => new DateOnly(today.Year, today.Month, 1),
                "startofyear" => new DateOnly(today.Year, 1, 1),
                _ => throw Error($"Функции {fn}() для дат нет; есть {string.Join(", ", FqlFields.DateFunctions)}", v)
            };
        }

        if (DateOnly.TryParseExact(v.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;

        var match = RelativeDate().Match(v.Text);
        if (!match.Success)
            throw Error($"«{v.Text}» — не дата; пишите 2026-09-01, -7d, 2w, today()", v);

        var n = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * (match.Groups[1].Value == "-" ? -1 : 1);
        return char.ToLowerInvariant(match.Groups[3].Value[0]) switch
        {
            'd' => today.AddDays(n),
            'w' => today.AddDays(7 * n),
            'm' => today.AddMonths(n),
            _ => today.AddYears(n)
        };
    }

    private static object Points(FqlValue v) =>
        decimal.TryParse(v.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var points)
            ? points
            : throw Error($"«{v.Text}» — не число", v);

    [GeneratedRegex(@"^(?:(\d+)d)?(?:(\d+)h)?(?:(\d+)m)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    /// <summary>Оценка: «30m», «2h», «1d2h»; день — 8 часов, как в интерфейсе. Число без единицы — ошибка: минуты или часы?</summary>
    private static object Duration(FqlValue v)
    {
        var match = DurationPattern().Match(v.Text);
        if (!match.Success || v.Text.Length == 0 || match.Groups.Values.Skip(1).All(g => !g.Success))
            throw Error($"«{v.Text}» — не оценка; пишите 30m, 2h, 1d", v);

        int Part(int group) => match.Groups[group].Success ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
        return Part(1) * 8 * 60 + Part(2) * 60 + Part(3);
    }

    private static TaskFilterNode Text(FqlClause c)
    {
        if (c.Operator != FqlOperator.Contains)
            throw Error("Текст ищется оператором «~»: text ~ \"слово\"", c.Field);

        var text = c.Values[0].Text.Trim();
        return text.Length > 0 ? new TaskFilterText(text) : throw Error("Пустая строка поиска", c.Values[0]);
    }

    // ---- общее ----

    private static TaskFilterNode Empty(FqlClause c, TaskFilterNullable field)
    {
        TaskFilterNode node = new TaskFilterIsEmpty(field);
        return c.Operator == FqlOperator.IsNotEmpty ? new TaskFilterNot(node) : node;
    }

    private static bool IsNegative(FqlClause c) => c.Operator is FqlOperator.NotEq or FqlOperator.NotIn;

    private static TaskFilterOp Op(FqlOperator op) => op switch
    {
        FqlOperator.Gt => TaskFilterOp.Gt,
        FqlOperator.Gte => TaskFilterOp.Gte,
        FqlOperator.Lt => TaskFilterOp.Lt,
        FqlOperator.Lte => TaskFilterOp.Lte,
        _ => TaskFilterOp.Eq
    };

    private static void EnsureOps(FqlClause c, params FqlOperator[] allowed)
    {
        if (!allowed.Contains(c.Operator))
            throw Error($"Оператор {Symbol(c.Operator)} к полю «{c.Field.Text}» не применим; можно: {string.Join(" ", allowed.Select(Symbol))}", c.Field);
    }

    private static string Symbol(FqlOperator op) => op switch
    {
        FqlOperator.Eq => "=",
        FqlOperator.NotEq => "!=",
        FqlOperator.Gt => ">",
        FqlOperator.Gte => ">=",
        FqlOperator.Lt => "<",
        FqlOperator.Lte => "<=",
        FqlOperator.In => "IN",
        FqlOperator.NotIn => "NOT IN",
        FqlOperator.IsEmpty => "IS EMPTY",
        FqlOperator.IsNotEmpty => "IS NOT EMPTY",
        _ => "~"
    };

    private static FqlException Error(string message, FqlToken at) => new(message, at.Position, at.Length);

    private static FqlException Error(string message, FqlValue at) => new(message, at.Position, at.Length);
}
