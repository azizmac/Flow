using Flow.Domain.Entities;
using Flow.Domain.Templates;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>
/// Чертёж конфигурации (docs/TZ_workflow_config.md §4, этап 3F): снимок проекта переносится на другой проект целиком,
/// повторное применение ничего не меняет, карта статусов решает, какой статус чем становится и куда уходят задачи.
/// </summary>
public class BoardBlueprintTests
{
    /// <summary>Все шаги по порядку, как у хендлера; лишние статусы удаляются сразу (задач у доски в тесте нет).</summary>
    private static BlueprintStatusResult Apply(Board board, BoardBlueprint blueprint, IReadOnlyDictionary<Guid, string>? map = null, bool archiveExtras = false)
    {
        var statuses = board.ApplyBlueprintStatuses(blueprint, map);
        foreach (var (statusId, moveTo) in statuses.Removals)
            board.RemoveStatus(statusId, moveTo);
        var types = board.ApplyBlueprintTypes(blueprint, archiveExtras);
        var fields = board.ApplyBlueprintFields(blueprint, types);
        board.ApplyBlueprintWorkflow(blueprint, statuses.StatusIds, types, fields);
        board.ApplyBlueprintScreens(blueprint, types, fields);
        return statuses;
    }

    public static TheoryData<Guid> BuiltIns() => new(BuiltInBoardTemplates.All.Select(t => t.Id));

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void Every_built_in_template_creates_a_project_that_looks_like_it(Guid templateId)
    {
        var template = BuiltInBoardTemplates.Find(templateId)!;

        var board = Board.Create("Из шаблона", "TPL", template.Blueprint);

        Assert.Equal(template.Blueprint.Statuses.Select(s => s.Name), board.Statuses.OrderBy(s => s.SortOrder).Select(s => s.Name));
        Assert.Single(board.Statuses, s => s.IsInitial);
        Assert.Equal(template.Blueprint.WorkflowMode, board.WorkflowMode);
        if (board.WorkflowMode == WorkflowMode.Restricted)
            Assert.Empty(board.DeadEnds());
        Assert.DoesNotContain(board.TaskTypes, t => t.IsArchived);
        Assert.Equal(template.Blueprint.CustomFields.Select(f => f.Key), board.CustomFields.Select(f => f.Key));
        Assert.Equal(template.Blueprint.Screens.Count, board.Screens.Count);
        var task = board.CreateTask("Первая");
        Assert.Equal(board.Statuses.Single(s => s.IsInitial).Id, task.StatusId);
    }

    [Fact]
    public void Built_in_templates_bring_their_limits_types_and_transitions()
    {
        var kanban = Board.Create("Поток", "KAN", BuiltInBoardTemplates.Find(BuiltInBoardTemplates.KanbanId)!.Blueprint);
        Assert.Equal(5, kanban.Statuses.Single(s => s.Name == "В работе").WipLimit);

        var scrum = Board.Create("Спринты", "SCR", BuiltInBoardTemplates.Find(BuiltInBoardTemplates.ScrumId)!.Blueprint);
        Assert.Equal("История", scrum.TaskTypes.Single(t => t.IsDefault).Name);
        Assert.DoesNotContain(scrum.TaskTypes, t => t.Kind == TaskTypeKind.Bug);

        var bugs = Board.Create("Баги", "BUG", BuiltInBoardTemplates.Find(BuiltInBoardTemplates.BugTrackerId)!.Blueprint);
        var severity = bugs.CustomFields.Single(f => f.Key == "severity");
        Assert.Equal(4, severity.Options.Count);
        Assert.Contains(bugs.Transitions, t => t.FromStatusId is null && bugs.Statuses.Single(s => s.Id == t.ToStatusId).Name == "Отклонена");
        Assert.Contains(bugs.Screens.Single().Fields, f => f.Field == ScreenFields.CustomPrefix + severity.Id);
    }

    private static Board Configured()
    {
        var board = Board.Create("Источник", "SRC");
        var ordered = board.Statuses.OrderBy(s => s.SortOrder).ToList();
        var review = board.AddStatus("Проверка QA", StatusType.InReview);
        board.RenameStatus(ordered[2].Id, "Ревью");
        board.SetStatusWipLimit(ordered[1].Id, 5);
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug);
        var env = board.AddCustomField("env", "Окружение", CustomFieldType.Select, ["prod", "stage"], taskTypeIds: [bug.Id]);
        board.SetWorkflow(WorkflowMode.Restricted,
        [
            new(ordered[0].Id, ordered[1].Id), new(ordered[1].Id, ordered[2].Id), new(ordered[2].Id, review.Id),
            new(review.Id, ordered[3].Id, Conditions: new TransitionConditions(RequireFields: [env.Id])), new(null, ordered[0].Id)
        ]);
        board.SetWorkflow(WorkflowMode.Free, [new(ordered[0].Id, ordered[3].Id)], bug.Id);
        board.SetScreen(bug.Id, ScreenContext.Detail, [new ScreenField("system:priority"), new ScreenField(ScreenFields.CustomPrefix + env.Id, true)]);
        return board;
    }

    [Fact]
    public void Snapshot_Recreates_The_Same_Configuration_On_Another_Board()
    {
        var source = Configured();
        var target = Board.Create("Цель", "DST");

        Apply(target, source.ToBlueprint());

        Assert.Equal(source.Statuses.OrderBy(s => s.SortOrder).Select(s => (s.Name, s.Type, s.IsInitial, s.IsFinal, s.WipLimit)),
            target.Statuses.OrderBy(s => s.SortOrder).Select(s => (s.Name, s.Type, s.IsInitial, s.IsFinal, s.WipLimit)));
        Assert.Equal(WorkflowMode.Restricted, target.WorkflowMode);
        var byName = target.Statuses.ToDictionary(s => s.Id, s => s.Name);
        Assert.Equal(5, target.TransitionsFor(null).Count);
        var env = target.CustomFields.Single(f => f.Key == "env");
        Assert.Equal(["prod", "stage"], env.Options.Select(o => o.Label));
        Assert.Contains(target.TransitionsFor(null), t => byName[t.ToStatusId] == "Сделана" && t.RequireFields.Single() == env.Id);
        var bug = target.TaskTypes.Single(t => t.Name == "Ошибка");
        Assert.Equal([bug.Id], env.TaskTypeIds);
        Assert.True(target.HasOwnWorkflow(bug.Id));
        Assert.Equal(ScreenFields.CustomPrefix + env.Id, target.Screens.Single().Fields[1].Field);
    }

    [Fact]
    public void Reapplying_The_Same_Blueprint_Changes_Nothing()
    {
        var board = Configured();
        var before = board.ToBlueprint();

        var result = Apply(board, before);

        Assert.Empty(result.Removals);
        Assert.Equal(before with { }, board.ToBlueprint(), new BlueprintComparer());
    }

    [Fact]
    public void Status_Map_Renames_Merges_And_Plans_Task_Moves()
    {
        var target = Board.Create("Цель", "DST");
        var ordered = target.Statuses.OrderBy(s => s.SortOrder).ToList(); // Не начата, В работе, На проверке, Сделана
        var blueprint = new BoardBlueprint(
            [
                new BlueprintStatus("todo", "В работе", StatusType.NotStarted, true, false),
                new BlueprintStatus("doing", "Не начата", StatusType.InProgress, false, false),
                new BlueprintStatus("done", "Готово", StatusType.Done, false, true)
            ],
            WorkflowMode.Free, [], [new BlueprintTaskType("t", "Задача", TaskTypeKind.Task, true)], [], []);

        // «Не начата» ↔ «В работе» меняются именами, «На проверке» сливается в «В работе» проекта, «Сделана» → «Готово».
        var result = Apply(target, blueprint, new Dictionary<Guid, string>
        {
            [ordered[0].Id] = "todo", [ordered[1].Id] = "doing", [ordered[2].Id] = "doing", [ordered[3].Id] = "done"
        }, archiveExtras: true);

        Assert.Equal(["В работе", "Не начата", "Готово"], target.Statuses.OrderBy(s => s.SortOrder).Select(s => s.Name));
        Assert.Equal(ordered[0].Id, result.StatusIds["todo"]);
        Assert.Equal((ordered[2].Id, ordered[1].Id), Assert.Single(result.Removals));
        Assert.True(target.GetStatus(ordered[0].Id).IsInitial);
        Assert.Equal(["Задача"], target.TaskTypes.Where(t => !t.IsArchived).Select(t => t.Name));
    }

    [Fact]
    public void Blueprint_Rules_And_Foreign_Field_Kind_Are_Refused()
    {
        var target = Board.Create("Цель", "DST");
        Assert.Throws<ArgumentException>(() => target.ApplyBlueprintStatuses(new BoardBlueprint(
            [new BlueprintStatus("a", "А", null, true, false)], WorkflowMode.Free, [], [], [], []), null));

        target.AddCustomField("env", "Окружение", CustomFieldType.Text);
        var blueprint = new BoardBlueprint([], WorkflowMode.Free, [], [], [new BlueprintField("env", "Окружение", CustomFieldType.Number)], []);
        Assert.Throws<InvalidOperationException>(() => target.ApplyBlueprintFields(blueprint, new Dictionary<string, Guid>()));
    }

    /// <summary>Чертежи сравниваются по содержимому списков, а не по ссылкам.</summary>
    private sealed class BlueprintComparer : IEqualityComparer<BoardBlueprint>
    {
        public bool Equals(BoardBlueprint? x, BoardBlueprint? y) =>
            x!.WorkflowMode == y!.WorkflowMode
            && x.Statuses.SequenceEqual(y.Statuses)
            && x.TaskTypes.SequenceEqual(y.TaskTypes)
            && x.Transitions.Count == y.Transitions.Count
            && x.Transitions.Select(t => (t.From, t.To, t.TypeKey, string.Join(",", t.RequireFieldKeys ?? [])))
                .OrderBy(t => t.ToString()).SequenceEqual(y.Transitions.Select(t => (t.From, t.To, t.TypeKey, string.Join(",", t.RequireFieldKeys ?? []))).OrderBy(t => t.ToString()))
            && x.CustomFields.Select(f => (f.Key, f.Name, f.Type, string.Join(",", f.Options ?? []), f.IsRequired, string.Join(",", f.TypeKeys ?? [])))
                .SequenceEqual(y.CustomFields.Select(f => (f.Key, f.Name, f.Type, string.Join(",", f.Options ?? []), f.IsRequired, string.Join(",", f.TypeKeys ?? []))))
            && x.Screens.Count == y.Screens.Count;

        public int GetHashCode(BoardBlueprint obj) => 0;
    }
}
