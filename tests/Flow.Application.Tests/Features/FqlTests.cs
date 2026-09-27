using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Fql;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using Xunit;
using TaskPriority = Flow.Domain.Entities.TaskPriority;

namespace Flow.Application.Tests.Features;

/// <summary>FQL (docs/TZ_task_views.md §7): токенизатор, парсер и биндер — чистые функции, без БД.</summary>
public class FqlTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 9, 30); // среда

    private sealed class Lookup : IFqlLookup
    {
        public List<Board> Boards { get; } = [];
        public Dictionary<string, Guid> Users { get; } = new() { ["ivan"] = Guid.NewGuid() };
        public List<TaskItem> Tasks { get; } = [];

        public Task<IReadOnlyList<Board>> VisibleBoardsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Board>>(Boards);

        public Task<IReadOnlyDictionary<string, Guid>> UsersByUsernameAsync(IReadOnlyCollection<string> usernames, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, Guid>>(Users.Where(u => usernames.Contains(u.Key)).ToDictionary(u => u.Key, u => u.Value));

        public Task<TaskItem?> TaskByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Tasks.FirstOrDefault(t => string.Equals(t.Code.Value, code, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<Guid>> DescendantsAsync(TaskItem root, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Tasks.Where(t => t.ParentId == root.Id).Select(t => t.Id).ToList());

        public Task<IReadOnlyList<Guid>> LinkedAsync(Guid taskId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>> BlockedByAsync(Guid taskId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public List<Sprint> Sprints { get; } = [];

        public Task<IReadOnlyList<Sprint>> SprintsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Sprint>>(Sprints);

        public List<Milestone> Milestones { get; } = [];

        public Task<IReadOnlyList<Milestone>> MilestonesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Milestone>>(Milestones);
    }

    private static (Lookup Lookup, Board Board) World()
    {
        var lookup = new Lookup();
        var board = Board.Create("Веб", "WEB");
        lookup.Boards.Add(board);
        lookup.Boards.Add(Board.Create("Мобилка", "MOB"));
        lookup.Tasks.Add(board.CreateTask("Эпик", typeId: board.TaskTypes.First(t => t.Kind == TaskTypeKind.Epic).Id));
        return (lookup, board);
    }

    private static Task<FqlBound> Bind(string query, Lookup lookup) => FqlBinder.BindAsync(query, lookup, Me, Today, CancellationToken.None);

    [Fact]
    public async Task Sprint_Binds_Names_Functions_And_Empty()
    {
        var (lookup, board) = World();
        var open = Sprint.Create(board.Id, "Альфа", null, 0);
        var closed = Sprint.Create(board.Id, "Бета", null, 1);
        closed.Start(Today, Today.AddDays(7), [], DateTime.UtcNow);
        closed.Complete([], null, DateTime.UtcNow);
        lookup.Sprints.AddRange([open, closed]);

        var byName = Assert.IsType<TaskFilterIn>((await Bind("sprint = альфа", lookup)).Filter);
        Assert.Equal((TaskFilterRef.Sprint, open.Id), (byName.Field, Assert.Single(byName.Ids)));
        Assert.Equal([closed.Id], Assert.IsType<TaskFilterIn>((await Bind("sprint in (closedSprints())", lookup)).Filter).Ids);
        Assert.Equal(TaskFilterNullable.Sprint, Assert.IsType<TaskFilterIsEmpty>((await Bind("sprint is empty", lookup)).Filter).Field);
        await Assert.ThrowsAsync<FqlException>(() => Bind("sprint = Гамма", lookup));
    }

    [Fact]
    public async Task Custom_Fields_Bind_By_Key_And_Type()
    {
        var (lookup, board) = World();
        var other = lookup.Boards[1];
        var sla = board.AddCustomField("sla", "SLA", CustomFieldType.Select, ["Gold", "Silver"]);
        var slaOther = other.AddCustomField("sla", "SLA", CustomFieldType.Select, ["Gold"]);
        board.AddCustomField("budget", "Бюджет", CustomFieldType.Number);
        board.AddCustomField("urgent", "Срочно", CustomFieldType.Checkbox);
        other.AddCustomField("budget_other", "Бюджет", CustomFieldType.Text);
        board.AddCustomField("mixed", "Смешанное", CustomFieldType.Number);
        other.AddCustomField("mixed", "Смешанное", CustomFieldType.Text);

        // Один ключ в двух проектах — оба поля, варианты с одинаковой подписью — оба Id.
        var select = Assert.IsType<TaskFilterCustomField>((await Bind("cf.sla = gold", lookup)).Filter);
        Assert.Equal([sla.Id, slaOther.Id], select.FieldIds);
        Assert.Equal([sla.Options[0].Id.ToString(), slaOther.Options[0].Id.ToString()], (IReadOnlyList<string>)select.Value!);

        var number = Assert.IsType<TaskFilterCustomField>((await Bind("cf.budget >= 10.5", lookup)).Filter);
        Assert.Equal((TaskFilterCustomOp.Gte, (object)10.5m), (number.Op, number.Value));
        Assert.IsType<TaskFilterNot>((await Bind("cf.budget is not empty", lookup)).Filter);
        Assert.IsType<TaskFilterOr>((await Bind("cf.urgent = false", lookup)).Filter); // «нет» или не заполнено

        await Assert.ThrowsAsync<FqlException>(() => Bind("cf.sla = Bronze", lookup));
        await Assert.ThrowsAsync<FqlException>(() => Bind("cf.budget = много", lookup));
        await Assert.ThrowsAsync<FqlException>(() => Bind("cf.mixed = 1", lookup));
        await Assert.ThrowsAsync<FqlException>(() => Bind("cf.nope = 1", lookup));
    }

    [Fact]
    public async Task Milestone_Binds_Names_Functions_And_Empty()
    {
        var (lookup, board) = World();
        var open = Milestone.Create(board.Id, "Релиз 1.0", null, null, 0);
        var closed = Milestone.Create(board.Id, "Бета", null, null, 1);
        closed.Close(DateTime.UtcNow);
        lookup.Milestones.AddRange([open, closed]);

        var byName = Assert.IsType<TaskFilterIn>((await Bind("milestone = \"релиз 1.0\"", lookup)).Filter);
        Assert.Equal((TaskFilterRef.Milestone, open.Id), (byName.Field, Assert.Single(byName.Ids)));
        Assert.Equal([closed.Id], Assert.IsType<TaskFilterIn>((await Bind("milestone in (closedMilestones())", lookup)).Filter).Ids);
        Assert.Equal([open.Id], Assert.IsType<TaskFilterIn>((await Bind("milestone in (openMilestones())", lookup)).Filter).Ids);
        Assert.Equal(TaskFilterNullable.Milestone, Assert.IsType<TaskFilterIsEmpty>((await Bind("milestone is empty", lookup)).Filter).Field);
    }

    [Fact]
    public void Parser_Respects_Precedence_Parentheses_And_Not()
    {
        var q = FqlParser.Parse("a = 1 OR b = 2 AND NOT (c = 3 or d = 4) order by due desc, rank");

        var or = Assert.IsType<FqlOr>(q.Where);
        Assert.IsType<FqlClause>(or.Items[0]);
        var and = Assert.IsType<FqlAnd>(or.Items[1]);
        var not = Assert.IsType<FqlNot>(and.Items[1]);
        Assert.IsType<FqlOr>(not.Item);
        Assert.Equal([("due", true), ("rank", false)], q.Orders.Select(o => (o.Field.Text, o.Descending)));
    }

    [Fact]
    public void Parser_Reads_Lists_Functions_Strings_And_Empty()
    {
        var q = FqlParser.Parse("status IN (\"В работе\", Сделана) AND parent = childrenOf(WEB-1) AND assignee = EMPTY AND due IS NOT EMPTY");
        var and = Assert.IsType<FqlAnd>(q.Where);

        var status = Assert.IsType<FqlClause>(and.Items[0]);
        Assert.Equal(FqlOperator.In, status.Operator);
        Assert.Equal(["В работе", "Сделана"], status.Values.Select(v => v.Text));
        Assert.True(status.Values[0].Quoted);

        var parent = Assert.IsType<FqlClause>(and.Items[1]);
        Assert.True(parent.Values[0].IsFunction("childrenof"));
        Assert.Equal("WEB-1", parent.Values[0].Args![0].Text);

        Assert.Equal(FqlOperator.IsEmpty, Assert.IsType<FqlClause>(and.Items[2]).Operator);
        Assert.Equal(FqlOperator.IsNotEmpty, Assert.IsType<FqlClause>(and.Items[3]).Operator);
    }

    [Theory]
    [InlineData("status = ", 9)]
    [InlineData("status IN (a, b", 15)]
    [InlineData("(status = a", 11)]
    [InlineData("status a", 7)]
    [InlineData("text ~ \"abc", 7)]
    [InlineData("a = 1 AND", 9)]
    [InlineData("a = 1 b = 2", 6)]
    public void Parser_Reports_Error_Position(string query, int position)
    {
        var error = Assert.Throws<FqlException>(() => FqlParser.Parse(query));
        Assert.Equal(position, error.Position);
    }

    [Fact]
    public void Empty_Query_Is_No_Filter()
    {
        var q = FqlParser.Parse("  ");
        Assert.Null(q.Where);
        Assert.Empty(q.Orders);
    }

    [Fact]
    public async Task Binder_Resolves_Names_To_Ids()
    {
        var (lookup, board) = World();

        var bound = await Bind("project = web AND status = \"сделана\" AND assignee in (@ivan, me()) AND key = WEB-1", lookup);

        var and = Assert.IsType<TaskFilterAnd>(bound.Filter);
        Assert.Equal([board.Id], Assert.IsType<TaskFilterIn>(and.Items[0]).Ids);
        var statuses = Assert.IsType<TaskFilterIn>(and.Items[1]);
        Assert.Equal(TaskFilterRef.Status, statuses.Field);
        Assert.Equal(2, statuses.Ids.Count); // «Сделана» есть в обоих проектах
        Assert.Equal(new HashSet<Guid> { lookup.Users["ivan"], Me }, Assert.IsType<TaskFilterIn>(and.Items[2]).Ids.ToHashSet());
        Assert.Equal(TaskFilterRef.Id, Assert.IsType<TaskFilterIn>(and.Items[3]).Field);
    }

    [Theory]
    [InlineData("project = NOPE", "Проект «NOPE» не найден", 10)]
    [InlineData("status = Отложена", "Статуса «Отложена» нет ни в одном проекте", 9)]
    [InlineData("assignee = @nobody", "Пользователь «@nobody» не найден", 11)]
    [InlineData("color = red", "Неизвестное поле «color»", 0)]
    [InlineData("team = A", "Поля «team» пока нет", 0)]
    [InlineData("milestone = M1", "Вехи «M1» нет ни в одном проекте", 12)]
    [InlineData("priority ~ high", "Оператор ~ к полю «priority» не применим", 0)]
    [InlineData("estimate > 5", "«5» — не оценка", 11)]
    [InlineData("due < tomorrow", "«tomorrow» — не дата", 6)]
    [InlineData("created IS EMPTY", "пустых значений не бывает", 0)]
    [InlineData("order by color", "По полю «color» сортировать нельзя", 9)]
    public async Task Binder_Errors_Point_At_The_Culprit(string query, string message, int position)
    {
        var (lookup, _) = World();

        var error = await Assert.ThrowsAsync<FqlException>(() => Bind(query, lookup));

        Assert.Contains(message, error.Message);
        Assert.Equal(position, error.Position);
    }

    [Fact]
    public async Task Negations_Wrap_In_Not_And_Empty_In_List_Adds_IsEmpty()
    {
        var (lookup, _) = World();

        var ne = await Bind("assignee != me()", lookup);
        Assert.IsType<TaskFilterIn>(Assert.IsType<TaskFilterNot>(ne.Filter).Item);

        var withEmpty = await Bind("assignee in (me(), EMPTY)", lookup);
        var or = Assert.IsType<TaskFilterOr>(withEmpty.Filter);
        Assert.Equal(TaskFilterNullable.Assignee, Assert.IsType<TaskFilterIsEmpty>(or.Items[1]).Field);

        var notIn = await Bind("typeKind not in (epic, ошибка)", lookup);
        Assert.Equal([TaskTypeKind.Epic, TaskTypeKind.Bug], Assert.IsType<TaskFilterTypeKinds>(Assert.IsType<TaskFilterNot>(notIn.Filter).Item).Kinds);
    }

    [Fact]
    public async Task Dates_Are_Relative_To_Today_And_Days_Are_Intervals_For_Moments()
    {
        var (lookup, _) = World();

        var due = Assert.IsType<TaskFilterCompare>((await Bind("due < 7d", lookup)).Filter);
        Assert.Equal((TaskFilterScalar.DueDate, TaskFilterOp.Lt, (object)new DateOnly(2026, 10, 7)), (due.Field, due.Op, due.Value));

        var week = Assert.IsType<TaskFilterCompare>((await Bind("start >= startOfWeek()", lookup)).Filter);
        Assert.Equal(new DateOnly(2026, 9, 28), week.Value);

        var createdDay = Assert.IsType<TaskFilterAnd>((await Bind("created = 2026-09-01", lookup)).Filter);
        Assert.Equal([new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)],
            createdDay.Items.Cast<TaskFilterCompare>().Select(c => (DateTime)c.Value));

        // «<= день» у момента — до конца этого дня.
        var until = Assert.IsType<TaskFilterCompare>((await Bind("updated <= -1d", lookup)).Filter);
        Assert.Equal((TaskFilterOp.Lt, (object)new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)), (until.Op, until.Value));
    }

    [Fact]
    public async Task Scalars_Priority_Estimate_Points()
    {
        var (lookup, _) = World();

        var priority = Assert.IsType<TaskFilterCompare>((await Bind("priority >= high", lookup)).Filter);
        Assert.Equal((TaskFilterOp.Gte, (object)(int)TaskPriority.High), (priority.Op, priority.Value));

        var estimate = Assert.IsType<TaskFilterCompare>((await Bind("estimate > 1d2h", lookup)).Filter);
        Assert.Equal(600, estimate.Value);

        var points = Assert.IsType<TaskFilterNot>((await Bind("points is not empty", lookup)).Filter);
        Assert.Equal(TaskFilterNullable.StoryPoints, Assert.IsType<TaskFilterIsEmpty>(points.Item).Field);
    }

    [Fact]
    public async Task Parent_And_Linked_Functions()
    {
        var (lookup, board) = World();
        var epic = lookup.Tasks[0];
        var child = board.CreateTask("История", typeId: board.TaskTypes.First(t => t.Kind == TaskTypeKind.Story).Id);
        child.SetParent(epic, board.TaskTypes.Single(t => t.Id == child.TypeId), board.TaskTypes.Single(t => t.Id == epic.TypeId));
        lookup.Tasks.Add(child);

        var direct = Assert.IsType<TaskFilterIn>((await Bind("parent = WEB-1", lookup)).Filter);
        Assert.Equal((TaskFilterRef.Parent, epic.Id), (direct.Field, direct.Ids.Single()));

        var subtree = Assert.IsType<TaskFilterIn>((await Bind("parent = childrenOf(WEB-1)", lookup)).Filter);
        Assert.Equal((TaskFilterRef.Id, child.Id), (subtree.Field, subtree.Ids.Single()));

        Assert.IsType<TaskFilterBlocked>((await Bind("linked = isBlocked()", lookup)).Filter);
        await Assert.ThrowsAsync<FqlException>(() => Bind("linked = WEB-1", lookup));
    }

    [Fact]
    public async Task Order_By_Maps_To_Sort_Fields()
    {
        var (lookup, _) = World();

        var bound = await Bind("ORDER BY priority DESC, key", lookup);

        Assert.Null(bound.Filter);
        Assert.Equal([new TaskOrder(TaskSortField.Priority, true), new TaskOrder(TaskSortField.Code, false)], bound.Orders);
    }
}
