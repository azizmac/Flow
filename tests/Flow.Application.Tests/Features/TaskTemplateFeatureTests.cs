using System.Text.Json;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.CustomFields;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.TaskTemplates;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Xunit;
using DomainFieldType = Flow.Domain.Entities.CustomFieldType;
using SharedPriority = Flow.Shared.Contracts.Tasks.TaskPriority;
using TaskTypeKind = Flow.Domain.Entities.TaskTypeKind;
using TaskActivityType = Flow.Domain.Entities.TaskActivityType;
using SearchIndexOperation = Flow.Application.Abstractions.SearchIndexOperation;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Шаблоны задач (docs/TZ_workflow_config.md §5, этап 3G): образец названия, проверка типов и полей, создание задачи
/// с чек-листом и подзадачами одной транзакцией, «сохранить задачу как шаблон». Права — в PermissionTests.
/// </summary>
public class TaskTemplateFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<Board> BoardAsync(IMediator mediator, FakeBoardRepository boards)
    {
        var id = (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "PRJ"), CancellationToken.None)).Response!.Id;
        return (await boards.GetByIdAsync(id, CancellationToken.None))!;
    }

    private static Guid Type(Board board, TaskTypeKind kind) => board.TaskTypes.Single(t => t.Kind == kind).Id;

    [Fact]
    public async Task Task_From_Template_Gets_Checklist_Subtasks_And_Next_Number()
    {
        var (mediator, boards, tasks, _, activities, search, _) = TestMediatorFactory.CreateWithJournal();
        var board = await BoardAsync(mediator, boards);
        var template = (await mediator.Send(new TaskTemplateCreateCommand(Owner, board.Id, new SaveTaskTemplateRequest(
            "Релиз", "Релиз {n} от {date}", Type(board, TaskTypeKind.Story), SharedPriority.High, "Шаги релиза",
            Checklist: ["Собрать", " ", "Выкатить"],
            Subtasks: [new("Тесты", null, ["Прогнать"]), new("Заметки", Type(board, TaskTypeKind.Task))])), CancellationToken.None))!;

        var today = DateTime.UtcNow.ToString("dd.MM.yyyy");
        Assert.Equal($"Релиз 1 от {today}", template.RenderedTitle);
        Assert.Equal(["Собрать", "Выкатить"], template.Checklist);

        var created = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, template.RenderedTitle, template.Description, null,
            template.TypeId, Flow.Domain.Entities.TaskPriority.High, TemplateId: template.Id), CancellationToken.None))!;

        Assert.Equal(2, created.ChecklistTotal);
        var children = (await tasks.GetChildrenAsync(created.Id, CancellationToken.None)).OrderBy(c => c.Rank, StringComparer.Ordinal).ToList();
        Assert.Equal(["Тесты", "Заметки"], children.Select(c => c.Title));
        Assert.Equal(Type(board, TaskTypeKind.Task), children[0].TypeId); // без типа — ближайший уровнем ниже истории, по умолчанию
        Assert.Equal("Прогнать", Assert.Single(children[0].Checklist).Text);
        var parent = (await tasks.GetByIdAsync(created.Id, CancellationToken.None))!;
        Assert.True(string.CompareOrdinal(parent.Rank, children[0].Rank) < 0);
        Assert.Equal(2, activities.ForTask(created.Id).Count(a => a.Type == TaskActivityType.ChildAdded));
        Assert.All(children, c => Assert.Contains(search.For(SearchSourceType.Task, c.Id), r => r.Operation == SearchIndexOperation.Upsert));

        var again = (await mediator.Send(new TaskTemplateListQuery(Owner, board.Id), CancellationToken.None))!.Single();
        Assert.Equal(1, again.UsageCount);
        Assert.StartsWith("Релиз 2 ", again.RenderedTitle);
    }

    [Fact]
    public async Task Subtask_Type_Must_Be_Below_And_Template_Of_Other_Project_Is_Refused()
    {
        var (mediator, boards, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator, boards);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskTemplateCreateCommand(Owner, board.Id, new SaveTaskTemplateRequest(
            "Плохой", "X", Type(board, TaskTypeKind.Task), Subtasks: [new("Эпик внутри", Type(board, TaskTypeKind.Epic))])), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(new TaskTemplateCreateCommand(Owner, board.Id,
            new SaveTaskTemplateRequest(" ", "X")), CancellationToken.None));

        var subtaskOnly = (await mediator.Send(new TaskTemplateCreateCommand(Owner, board.Id,
            new SaveTaskTemplateRequest("Мелочь", "Мелочь", Subtasks: [new("Ещё ниже")])), CancellationToken.None))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskTemplateCreateCommand(Owner, board.Id,
            new SaveTaskTemplateRequest("мелочь", "Дубль")), CancellationToken.None));
        // Подзадача у подзадачи не бывает: такую задачу по шаблону не создать.
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskCreateCommand(Owner, board.Id, "Мелочь", null, null,
            Type(board, TaskTypeKind.Subtask), TemplateId: subtaskOnly.Id), CancellationToken.None));

        var otherId = (await mediator.Send(new BoardCreateCommand(Owner, "Другой", "OTH"), CancellationToken.None)).Response!.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskCreateCommand(Owner, otherId, "Чужой", null, null,
            TemplateId: subtaskOnly.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Saved_From_Task_Keeps_Fields_Checklist_And_Direct_Subtasks()
    {
        var (mediator, boards, tasks, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator, boards);
        var withField = (await mediator.Send(new CustomFieldCreateCommand(Owner, board.Id, "env", "Окружение", DomainFieldType.Text), CancellationToken.None))!;
        var env = withField.CustomFields.Single().Id;
        var parent = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Выпуск", "Описание", null, Type(board, TaskTypeKind.Story),
            CustomFields: new Dictionary<Guid, JsonElement?> { [env] = JsonSerializer.SerializeToElement("prod") }), CancellationToken.None))!;
        var parentItem = (await tasks.GetByIdAsync(parent.Id, CancellationToken.None))!;
        parentItem.AddChecklistItem("Проверить");
        await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Шаг", null, null, Type(board, TaskTypeKind.Task), ParentId: parent.Id), CancellationToken.None);

        var template = (await mediator.Send(new TaskTemplateFromTaskCommand(Owner, parent.Id, "Выпуск"), CancellationToken.None))!;

        Assert.Equal("Выпуск", template.TitlePattern);
        Assert.Equal(Type(board, TaskTypeKind.Story), template.TypeId);
        Assert.Equal("prod", template.CustomFields[env].GetString());
        Assert.Equal(["Проверить"], template.Checklist);
        Assert.Equal("Шаг", Assert.Single(template.Subtasks).Title);
        Assert.Null(await mediator.Send(new TaskTemplateFromTaskCommand(Owner, Guid.NewGuid(), "X"), CancellationToken.None));

        var updated = (await mediator.Send(new TaskTemplateUpdateCommand(Owner, template.Id, new SaveTaskTemplateRequest("Выпуск", "Выпуск {date}")),
            CancellationToken.None))!;
        Assert.Empty(updated.Subtasks);
        Assert.True(await mediator.Send(new TaskTemplateDeleteCommand(Owner, template.Id), CancellationToken.None));
        Assert.Empty((await mediator.Send(new TaskTemplateListQuery(Owner, board.Id), CancellationToken.None))!);
    }
}
