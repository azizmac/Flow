using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Workflow проекта (docs/TZ_workflow_config.md §2): граф, условия, тупики, создание в статусе.</summary>
public class WorkflowTests
{
    private static (Board Board, Guid Todo, Guid Doing, Guid Review, Guid Done) NewBoard()
    {
        var board = Board.Create("Проект", "PRJ");
        Guid Id(string name) => board.Statuses.Single(s => s.Name == name).Id;
        return (board, Id("Не начата"), Id("В работе"), Id("На проверке"), Id("Сделана"));
    }

    private static readonly TransitionContext Anyone = new(ProjectRole.Member, false, true, true);

    [Fact]
    public void Free_Mode_Allows_Everything()
    {
        var (board, todo, _, _, done) = NewBoard();

        Assert.True(board.CheckTransition(todo, done, Anyone).Allowed);
    }

    [Fact]
    public void Restricted_Mode_Follows_Graph_And_Global_Transitions()
    {
        var (board, todo, doing, review, done) = NewBoard();
        board.SetWorkflow(WorkflowMode.Restricted,
        [
            new(todo, doing), new(doing, review), new(review, done), new(review, doing),
            new(null, todo, "Вернуть в очередь")
        ]);

        Assert.True(board.CheckTransition(todo, doing, Anyone).Allowed);
        Assert.True(board.CheckTransition(done, todo, Anyone).Allowed); // «из любого»

        var denied = board.CheckTransition(todo, done, Anyone);
        Assert.False(denied.Allowed);
        Assert.Contains("«Не начата» → «Сделана»", denied.Reasons.Single());
    }

    [Fact]
    public void Conditions_Are_Checked_And_Any_Matching_Transition_Is_Enough()
    {
        var (board, todo, doing, review, done) = NewBoard();
        board.SetWorkflow(WorkflowMode.Restricted,
        [
            new(todo, doing, Conditions: new TransitionConditions(RequireAssignee: true)),
            new(doing, review), new(review, todo),
            new(review, done, Conditions: new TransitionConditions(MinRole: ProjectRole.Developer, RequireChildrenDone: true, RequireChecklistDone: true)),
            new(null, done, "Закрыть админом", new TransitionConditions(MinRole: ProjectRole.Admin))
        ]);

        Assert.Equal(["Сначала назначьте исполнителя"], board.CheckTransition(todo, doing, Anyone).Reasons);
        Assert.True(board.CheckTransition(todo, doing, Anyone with { HasAssignee = true }).Allowed);

        var weak = board.CheckTransition(review, done, new TransitionContext(ProjectRole.Member, true, false, false));
        Assert.Equal(3, weak.Reasons.Count);

        // Прямой переход не пускает, но «из любого» для администратора — пускает.
        Assert.True(board.CheckTransition(review, done, new TransitionContext(ProjectRole.Admin, true, false, false)).Allowed);
    }

    [Fact]
    public void Restricted_Mode_Rejects_Dead_Ends()
    {
        var (board, todo, doing, review, done) = NewBoard();

        var error = Assert.Throws<InvalidOperationException>(() =>
            board.SetWorkflow(WorkflowMode.Restricted, [new(todo, doing), new(doing, done)]));
        Assert.Contains("«На проверке»", error.Message);
        Assert.Equal(WorkflowMode.Free, board.WorkflowMode);

        // «Из любого» закрывает тупики разом; финальным статусам исходящие не нужны.
        board.SetWorkflow(WorkflowMode.Restricted, [new(null, done), new(null, todo)]);
        Assert.Empty(board.DeadEnds());

        // В Free тупики допустимы — граф можно готовить заранее.
        board.SetWorkflow(WorkflowMode.Free, [new(todo, doing)]);
        Assert.NotEmpty(board.DeadEnds());
        Assert.Equal(review, board.DeadEnds().Last().Id);
    }

    [Fact]
    public void SetWorkflow_Validates_Statuses_Self_And_Duplicates()
    {
        var (board, todo, doing, _, _) = NewBoard();
        var other = Board.Create("Другой", "OTH").Statuses.First().Id;

        Assert.Throws<InvalidOperationException>(() => board.SetWorkflow(WorkflowMode.Free, [new(todo, other)]));
        Assert.Throws<InvalidOperationException>(() => board.SetWorkflow(WorkflowMode.Free, [new(todo, todo)]));
        Assert.Throws<InvalidOperationException>(() => board.SetWorkflow(WorkflowMode.Free, [new(todo, doing), new(todo, doing)]));
        Assert.Throws<ArgumentException>(() => board.SetWorkflow(WorkflowMode.Free, [new(todo, doing, new string('x', 61))]));
    }

    [Fact]
    public void Create_Directly_In_Status_Needs_Global_Transition_When_Restricted()
    {
        var (board, todo, doing, review, done) = NewBoard();
        board.SetWorkflow(WorkflowMode.Restricted, [new(todo, doing), new(doing, review), new(review, done), new(null, doing)]);

        Assert.Equal(doing, board.CreateTask("В работу сразу", statusId: doing).StatusId);
        Assert.Equal(todo, board.CreateTask("Начальный можно всегда", statusId: todo).StatusId);
        Assert.Throws<InvalidOperationException>(() => board.CreateTask("Сразу сделана", statusId: done));
    }

    [Fact]
    public void Removing_Status_Drops_Its_Transitions()
    {
        var (board, todo, doing, review, done) = NewBoard();
        board.SetWorkflow(WorkflowMode.Free, [new(todo, review), new(review, done), new(doing, done)]);

        board.RemoveStatus(review, doing);

        Assert.Equal([(doing, done)], board.Transitions.Select(t => (t.FromStatusId!.Value, t.ToStatusId)));
    }

    /// <summary>Раскладка графа (этап 3D): свои статусы, 0…5000, непереданный статус и пустой список — автораскладка.</summary>
    [Fact]
    public void Status_Layout_Is_Set_Whole_And_Validated()
    {
        var board = Board.Create("Проект", "PRJ");
        var statuses = board.Statuses.OrderBy(s => s.SortOrder).ToList();

        board.SetStatusLayout([(statuses[0].Id, 100.04, 80), (statuses[1].Id, 300, 200)]);
        Assert.Equal(((double?)100.0, (double?)80), (statuses[0].GraphX, statuses[0].GraphY));
        Assert.Null(statuses[2].GraphX);

        Assert.Throws<InvalidOperationException>(() => board.SetStatusLayout([(Guid.NewGuid(), 1, 1)]));
        Assert.Throws<ArgumentException>(() => board.SetStatusLayout([(statuses[0].Id, -1, 1)]));
        Assert.Throws<ArgumentException>(() => board.SetStatusLayout([(statuses[0].Id, 1, 5001)]));
        Assert.Equal((double?)300, statuses[1].GraphX);

        board.SetStatusLayout([]);
        Assert.All(statuses, s => Assert.Null(s.GraphX));
    }

    /// <summary>Workflow по типу задачи (этап 3E): свой у типа замещает проектный только для этого типа.</summary>
    [Fact]
    public void Task_Type_Own_Workflow_Overrides_Project_Only_For_That_Type()
    {
        var board = Board.Create("Проект", "PRJ");
        var statuses = board.Statuses.OrderBy(s => s.SortOrder).ToList();
        var (todo, doing, done) = (statuses[0].Id, statuses[1].Id, statuses[3].Id);
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug);
        var task = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Task);
        var ctx = new TransitionContext(ProjectRole.Admin, true, true, true);

        board.SetWorkflow(WorkflowMode.Restricted, [new(todo, doing), new(doing, done), new(null, todo), new(statuses[2].Id, done)], bug.Id);
        Assert.True(board.HasOwnWorkflow(bug.Id));
        Assert.Equal(WorkflowMode.Free, board.WorkflowMode);
        Assert.False(board.CheckTransition(todo, done, ctx, bug.Id).Allowed);
        Assert.Contains("типа «Ошибка»", board.CheckTransition(todo, done, ctx, bug.Id).Reasons.Single());
        Assert.True(board.CheckTransition(todo, done, ctx, task.Id).Allowed);
        Assert.Throws<InvalidOperationException>(() => board.CreateTask("Баг", statusId: doing, typeId: bug.Id));
        board.CreateTask("Задача", statusId: doing, typeId: task.Id);

        // Проектный workflow и workflow типа живут рядом: замена одного не трогает другой.
        board.SetWorkflow(WorkflowMode.Restricted, [new(todo, done), new(doing, done), new(statuses[2].Id, done)]);
        Assert.Equal(4, board.TransitionsFor(bug.Id).Count);
        Assert.Equal(3, board.TransitionsFor(task.Id).Count);

        board.ResetTypeWorkflow(bug.Id);
        Assert.False(board.HasOwnWorkflow(bug.Id));
        Assert.True(board.CheckTransition(todo, done, ctx, bug.Id).Allowed);
        Assert.Equal(3, board.Transitions.Count);
    }
}
