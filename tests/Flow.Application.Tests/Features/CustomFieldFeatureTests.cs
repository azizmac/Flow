using System.Text.Json;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.CustomFields;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Xunit;
using SharedFieldType = Flow.Shared.Contracts.CustomFields.CustomFieldType;
using SharedTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Пользовательские поля в Application (docs/TZ_task_model.md §4): определения, значения с журналом и поиском,
/// обязательные при создании и смене типа, «пользователь активен». Правила значения — Flow.Domain.Tests, SQL — Infrastructure.
/// </summary>
public class CustomFieldFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static JsonElement? Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task<BoardResponse> BoardAsync(IMediator mediator) =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "PRJ"), CancellationToken.None)).Response!;

    [Fact]
    public async Task Definitions_Are_Part_Of_Board_Response()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);

        var created = (await mediator.Send(new CustomFieldCreateCommand(Owner, board.Id, "sla", "SLA", CustomFieldType.Select, ["Gold", "Silver"]), CancellationToken.None))!;
        var field = Assert.Single(created.CustomFields!);
        Assert.Equal(("sla", SharedFieldType.Select, 2), (field.Key, field.Type, field.Options.Count));

        var renamed = (await mediator.Send(new CustomFieldUpdateCommand(Owner, board.Id, field.Id, Name: "Уровень SLA",
            Options: [(field.Options[0].Id, "Platinum", "#C0C0C0")], IsArchived: true), CancellationToken.None))!;
        var updated = Assert.Single(renamed.CustomFields!);
        Assert.Equal(("Уровень SLA", "Platinum", true), (updated.Name, Assert.Single(updated.Options).Label, updated.IsArchived));
        Assert.Equal(field.Options[0].Id, updated.Options[0].Id);
    }

    [Fact]
    public async Task Values_Write_Journal_Search_And_Check_Users()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await BoardAsync(mediator);
        var withFields = (await mediator.Send(new CustomFieldCreateCommand(Owner, board.Id, "steps", "Шаги", CustomFieldType.LongText), CancellationToken.None))!;
        withFields = (await mediator.Send(new CustomFieldCreateCommand(Owner, board.Id, "reviewer", "Ревьюер", CustomFieldType.User), CancellationToken.None))!;
        var steps = withFields.CustomFields![0].Id;
        var reviewer = withFields.CustomFields![1].Id;
        var task = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Задача", null, null), CancellationToken.None))!.Id;

        var result = await mediator.Send(new TaskSetCustomFieldsCommand(Owner, task, new Dictionary<Guid, JsonElement?>
        {
            [steps] = Json("\"Открыть и нажать\""),
            [reviewer] = Json($"\"{Owner}\"")
        }), CancellationToken.None);

        Assert.Equal("Открыть и нажать", result.Response!.CustomFields![steps].GetString());
        var entries = activities.ForTask(task).Where(a => a.Type == TaskActivityType.CustomFieldChanged).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains(steps.ToString(), entries[0].NewValue);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskSetCustomFieldsCommand(Owner, task,
            new Dictionary<Guid, JsonElement?> { [reviewer] = Json($"\"{Guid.NewGuid()}\"") }), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskSetCustomFieldsCommand(Owner, task,
            new Dictionary<Guid, JsonElement?> { [Guid.NewGuid()] = Json("1") }), CancellationToken.None));
    }

    [Fact]
    public async Task Required_Fields_Are_Checked_On_Create_And_Type_Change()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var bug = board.TaskTypes.Single(t => t.Kind == SharedTypeKind.Bug).Id;
        var withField = (await mediator.Send(new CustomFieldCreateCommand(Owner, board.Id, "severity", "Серьёзность", CustomFieldType.Text,
            IsRequired: true, TaskTypeIds: [bug]), CancellationToken.None))!;
        var severity = withField.CustomFields![0].Id;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new TaskCreateCommand(Owner, board.Id, "Ошибка", null, null, bug), CancellationToken.None));
        Assert.Contains("«Серьёзность»", error.Message);

        var created = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Ошибка", null, null, bug,
            CustomFields: new Dictionary<Guid, JsonElement?> { [severity] = Json("\"высокая\"") }), CancellationToken.None))!;
        Assert.Equal("высокая", created.CustomFields![severity].GetString());

        // Обычная задача заполнять поле ошибки не обязана — пока не станет ошибкой.
        var plain = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Задача", null, null), CancellationToken.None))!.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskUpdateCommand(Owner, plain, null, null, null, TypeId: bug), CancellationToken.None));
    }
}
