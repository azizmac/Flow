using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Templates;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Domain.Templates;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using MediatR;
using Xunit;
using CustomFieldType = Flow.Domain.Entities.CustomFieldType;
using DomainStatusType = Flow.Domain.Entities.StatusType;
using TaskTypeKind = Flow.Domain.Entities.TaskTypeKind;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Шаблоны проектов и перенос конфигурации (docs/TZ_workflow_config.md §4, этап 3F): создание по каждому встроенному
/// шаблону, снимок проекта с образцами задач, чтение старой версии чертежа, карта статусов с переносом задач,
/// идемпотентность и частичный отказ. Сами правила чертежа — в Flow.Domain.Tests (BoardBlueprintTests).
/// </summary>
public class BoardTemplateFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<Board> CreateAsync(IMediator mediator, FakeBoardRepository boards, string key, Guid? templateId = null)
    {
        var response = (await mediator.Send(new BoardCreateCommand(Owner, "Проект " + key, key, templateId), CancellationToken.None)).Response!;
        return (await boards.GetByIdAsync(response.Id, CancellationToken.None))!;
    }

    private static User Developer(FakeUserRepository users)
    {
        var user = User.Create("dev", "dev@example.com", "Дев", "Разработчик");
        user.ChangeRole(UserRole.Developer);
        user.MarkActive();
        users.Add(user);
        return user;
    }

    public static TheoryData<Guid> BuiltIns() => new(BuiltInBoardTemplates.All.Select(t => t.Id));

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public async Task Project_Is_Created_From_Every_Built_In_Template(Guid templateId)
    {
        var (mediator, boards, _, _) = TestMediatorFactory.Create();

        var response = (await mediator.Send(new BoardCreateCommand(Owner, "Из шаблона", "TPL", templateId), CancellationToken.None)).Response!;

        var template = BuiltInBoardTemplates.Find(templateId)!;
        Assert.Equal(template.Blueprint.Statuses.Select(s => s.Name), response.Statuses.Select(s => s.Name));
        Assert.Equal((Flow.Shared.Contracts.Boards.WorkflowMode)(int)template.Blueprint.WorkflowMode, response.WorkflowMode);
        Assert.Equal(template.Blueprint.CustomFields.Select(f => f.Key), response.CustomFields.Select(f => f.Key));
        Assert.NotNull(await boards.GetByIdAsync(response.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_Template_Is_Rejected()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new BoardCreateCommand(Owner, "Проект", "PRJ", Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Saved_Template_Recreates_Configuration_And_Sample_Tasks()
    {
        var (mediator, boards, tasks, _, activities, _, _) = TestMediatorFactory.CreateWithJournal();
        var source = await CreateAsync(mediator, boards, "SRC");
        var review = source.Statuses.Single(s => s.Type == DomainStatusType.InReview);
        source.RenameStatus(review.Id, "Ревью");
        source.AddStatus("Отменена", DomainStatusType.Done, isFinal: true);
        source.AddCustomField("env", "Окружение", CustomFieldType.Text);
        var bug = source.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug);
        await mediator.Send(new TaskCreateCommand(Owner, source.Id, "Первая", "Описание", null, bug.Id), CancellationToken.None);
        await mediator.Send(new TaskCreateCommand(Owner, source.Id, "Вторая", null, null), CancellationToken.None);

        var template = await mediator.Send(new BoardTemplateSaveCommand(Owner, source.Id, " Мой шаблон ", "Для команды", IncludeTasks: true), CancellationToken.None);
        Assert.Equal("Мой шаблон", template!.Name);
        Assert.False(template.IsBuiltIn);
        Assert.Equal(2, template.SampleTaskCount);
        Assert.Contains("Ревью", template.Statuses);

        var copy = await CreateAsync(mediator, boards, "CPY", template.Id);

        Assert.Equal(source.Statuses.OrderBy(s => s.SortOrder).Select(s => s.Name), copy.Statuses.OrderBy(s => s.SortOrder).Select(s => s.Name));
        Assert.Single(copy.CustomFields, f => f.Key == "env");
        var samples = await tasks.GetByBoardIdAsync(copy.Id, null, CancellationToken.None);
        Assert.Equal(["Первая", "Вторая"], samples.OrderBy(t => t.Rank, StringComparer.Ordinal).Select(t => t.Title));
        Assert.Equal(copy.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug).Id, samples.Single(t => t.Title == "Первая").TypeId);
        Assert.All(samples, t => Assert.Equal(copy.Statuses.Single(s => s.IsInitial).Id, t.StatusId));
        Assert.All(samples, t => Assert.Contains(activities.ForTask(t.Id), a => a.Type == TaskActivityType.Created));

        var list = await mediator.Send(new BoardTemplateListQuery(Owner), CancellationToken.None);
        Assert.Equal(BuiltInBoardTemplates.All.Count + 1, list.Count);
        Assert.True(await mediator.Send(new BoardTemplateDeleteCommand(Owner, template.Id), CancellationToken.None));
        Assert.False(await mediator.Send(new BoardTemplateDeleteCommand(Owner, template.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Built_In_Template_Cannot_Be_Deleted_And_Developer_Cannot_Save()
    {
        var (mediator, boards, _, users) = TestMediatorFactory.Create();
        var board = await CreateAsync(mediator, boards, "PRJ");
        var developer = Developer(users);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new BoardTemplateDeleteCommand(Owner, BuiltInBoardTemplates.KanbanId), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new BoardTemplateSaveCommand(developer.Id, board.Id, "Шаблон", null, false), CancellationToken.None));
        Assert.Equal(BuiltInBoardTemplates.All.Count, (await mediator.Send(new BoardTemplateListQuery(developer.Id), CancellationToken.None)).Count);
    }

    [Fact]
    public void Old_Minimal_Payload_Is_Read_And_Newer_Version_Is_Refused()
    {
        const string v1 = """
            {"statuses":[{"key":"a","name":"Открыта","type":"NotStarted","isInitial":true,"isFinal":false},
                         {"key":"b","name":"Закрыта","type":"Done","isInitial":false,"isFinal":true}],
             "workflowMode":"Free"}
            """;

        var blueprint = BoardBlueprintJson.Deserialize(v1, 1);

        Assert.Equal(["Открыта", "Закрыта"], blueprint.Statuses.Select(s => s.Name));
        Assert.Empty(blueprint.Transitions);
        Assert.Empty(blueprint.SampleTasks);
        var board = Board.Create("Старый", "OLD", blueprint);
        Assert.Equal(DefaultTaskTypes.All.Count, board.TaskTypes.Count);
        Assert.Throws<InvalidOperationException>(() => BoardBlueprintJson.Deserialize(v1, BoardBlueprint.CurrentVersion + 1));

        var roundTrip = BoardBlueprintJson.Deserialize(BoardBlueprintJson.Serialize(BuiltInBoardTemplates.All[3].Blueprint), BoardBlueprint.CurrentVersion);
        Assert.Equal(BuiltInBoardTemplates.All[3].Blueprint.CustomFields.Count, roundTrip.CustomFields.Count);
        Assert.Contains("\"Restricted\"", BoardBlueprintJson.Serialize(roundTrip));
    }

    [Fact]
    public async Task Apply_Maps_Statuses_Moves_Tasks_And_Repeats_As_No_Op()
    {
        var (mediator, boards, tasks, _, activities, search, unitOfWork) = TestMediatorFactory.CreateWithJournal();
        var source = await CreateAsync(mediator, boards, "SRC");
        var sourceReview = source.Statuses.Single(s => s.Type == DomainStatusType.InReview);
        source.RenameStatus(sourceReview.Id, "Ревью");
        source.AddCustomField("env", "Окружение", CustomFieldType.Text);

        var target = await CreateAsync(mediator, boards, "TGT");
        var extra = target.AddStatus("Ожидание", null);
        var task = (await mediator.Send(new TaskCreateCommand(Owner, target.Id, "Ждёт", null, extra.Id), CancellationToken.None))!;
        var targetReview = target.Statuses.Single(s => s.Type == DomainStatusType.InReview);
        var map = new Dictionary<Guid, Guid> { [extra.Id] = sourceReview.Id };
        search.Clear();

        var preview = await mediator.Send(new BoardApplyConfigPreviewQuery(Owner, source.Id, [new ApplyConfigTarget(target.Id, map)], ConfigParts.All),
            CancellationToken.None);
        var targetPreview = preview!.Targets.Single();
        Assert.Equal(["На проверке → Ревью"], targetPreview.StatusesRenamed);
        Assert.Equal(["Ожидание (1 → Ревью)"], targetPreview.StatusesRemoved);
        Assert.Equal(1, targetPreview.TasksMoved);
        Assert.Equal(["Окружение"], targetPreview.CustomFieldsAdded);
        Assert.False(targetPreview.StatusMap.Single(m => m.StatusId == extra.Id).Kept);

        var result = await mediator.Send(new BoardApplyConfigCommand(Owner, source.Id, [new ApplyConfigTarget(target.Id, map)], ConfigParts.All),
            CancellationToken.None);

        var applied = result!.Results.Single();
        Assert.True(applied is { Success: true, Changed: true, TasksMoved: 1 });
        Assert.DoesNotContain(target.Statuses, s => s.Id == extra.Id);
        Assert.Equal("Ревью", target.GetStatus(targetReview.Id).Name);
        Assert.Equal(targetReview.Id, (await tasks.GetByIdAsync(task.Id, CancellationToken.None))!.StatusId);
        Assert.Contains(activities.ForTask(task.Id), a => a.Type == TaskActivityType.StatusChanged && a.NewValue == targetReview.Id.ToString());
        Assert.Contains(search.For(SearchSourceType.Task, task.Id), r => r.Operation == SearchIndexOperation.Upsert);
        Assert.Single(target.CustomFields, f => f.Key == "env");

        var discards = unitOfWork.DiscardCount;
        var again = await mediator.Send(new BoardApplyConfigCommand(Owner, source.Id, [new ApplyConfigTarget(target.Id)], ConfigParts.All), CancellationToken.None);
        Assert.True(again!.Results.Single() is { Success: true, Changed: false });
        Assert.Equal(discards + 1, unitOfWork.DiscardCount);
    }

    [Fact]
    public async Task One_Failing_Target_Does_Not_Stop_The_Others()
    {
        var (mediator, boards, _, _, _, _, unitOfWork) = TestMediatorFactory.CreateWithJournal();
        var source = await CreateAsync(mediator, boards, "SRC");
        source.AddCustomField("env", "Окружение", CustomFieldType.Text);
        var good = await CreateAsync(mediator, boards, "GOOD");
        var bad = await CreateAsync(mediator, boards, "BAD");
        bad.AddCustomField("env", "Окружение", CustomFieldType.Number);

        var result = await mediator.Send(new BoardApplyConfigCommand(Owner, source.Id,
            [new ApplyConfigTarget(bad.Id), new ApplyConfigTarget(good.Id), new ApplyConfigTarget(source.Id)], ConfigParts.CustomFields), CancellationToken.None);

        Assert.Collection(result!.Results,
            r => Assert.True(r is { Success: false } && r.Error!.Contains("другого вида")),
            r => Assert.True(r is { Success: true, Changed: true }),
            r => Assert.Equal("Это проект-источник.", r.Error));
        Assert.Single(good.CustomFields);
        Assert.True(unitOfWork.DiscardCount >= 1);
        var preview = await mediator.Send(new BoardApplyConfigPreviewQuery(Owner, source.Id, [new ApplyConfigTarget(bad.Id)], ConfigParts.CustomFields),
            CancellationToken.None);
        Assert.Contains("другого вида", preview!.Targets.Single().Error);
    }

    [Fact]
    public async Task Apply_Needs_Config_Rights_In_Every_Target_Before_Any_Change()
    {
        var (mediator, boards, _, users) = TestMediatorFactory.Create();
        var source = await CreateAsync(mediator, boards, "SRC");
        var target = await CreateAsync(mediator, boards, "TGT");
        var developer = Developer(users);

        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(
            new BoardApplyConfigCommand(developer.Id, source.Id, [new ApplyConfigTarget(target.Id)], ConfigParts.All), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(
            new BoardApplyConfigCommand(Owner, source.Id, [new ApplyConfigTarget(target.Id)], ConfigParts.None), CancellationToken.None));
        Assert.Null(await mediator.Send(
            new BoardApplyConfigCommand(Owner, Guid.NewGuid(), [new ApplyConfigTarget(target.Id)], ConfigParts.All), CancellationToken.None));
    }
}
