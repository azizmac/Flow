using System.Text.Json;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.ScreenSetCommand;
using Flow.Application.Features.Boards.Workflow;
using Flow.Application.Features.CustomFields;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Exceptions;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Xunit;
using DomainContext = Flow.Domain.Entities.ScreenContext;
using SharedWorkflowMode = Flow.Shared.Contracts.Boards.WorkflowMode;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Экраны и обязательные поля в Application (этап 3C): «обязательное» экрана создания проверяет сервер, исполнитель
/// ставится при создании по правилам назначения, переход с RequireFields не пускает без значения.
/// </summary>
public class ScreenFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<BoardResponse> BoardAsync(IMediator mediator) =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "PRJ"), CancellationToken.None)).Response!;

    [Fact]
    public async Task Create_Screen_Required_Assignee_Is_Checked_By_Server()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        await mediator.Send(new ScreenSetCommand(Owner, board.Id, null, DomainContext.Create,
            [new ScreenFieldDto("system:assignee", Required: true)]), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new TaskCreateCommand(Owner, board.Id, "Без исполнителя", null, null), CancellationToken.None));
        Assert.Contains("«Исполнитель»", error.Message);

        var created = await mediator.Send(new TaskCreateCommand(Owner, board.Id, "С исполнителем", null, null, AssigneeId: Owner), CancellationToken.None);
        Assert.Equal(Owner, created!.AssigneeId);

        // Member назначает при создании только себя — то же правило, что у PATCH /assignee.
        var member = User.Create("member", "member@example.com", "Имя", "Фамилия");
        member.MarkActive();
        users.Add(member);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new TaskCreateCommand(member.Id, board.Id, "Чужой", null, null, AssigneeId: Owner), CancellationToken.None));
    }

    [Fact]
    public async Task Transition_With_Required_Field_Is_Refused_Until_Filled()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var withField = (await mediator.Send(new CustomFieldCreateCommand(Owner, board.Id, "steps", "Шаги", CustomFieldType.Text), CancellationToken.None))!;
        var steps = withField.CustomFields![0].Id;
        var initial = board.Statuses.Single(s => s.IsInitial).Id;
        var done = board.Statuses.Single(s => s.IsFinal).Id;
        await mediator.Send(new WorkflowSetCommand(Owner, board.Id, SharedWorkflowMode.Restricted, [
            new TransitionRequest(null, done, Conditions: new TransitionConditionsDto(RequireFields: [steps])),
            new TransitionRequest(done, initial)
        ]), CancellationToken.None);
        var task = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Задача", null, null), CancellationToken.None))!.Id;

        var refused = await mediator.Send(new TaskUpdateCommand(Owner, task, null, null, done), CancellationToken.None);
        Assert.Contains("Заполните поле «Шаги»", refused.Reasons!);

        await mediator.Send(new TaskSetCustomFieldsCommand(Owner, task, new Dictionary<Guid, JsonElement?>
        {
            [steps] = JsonDocument.Parse("\"1. Открыть\"").RootElement.Clone()
        }), CancellationToken.None);
        Assert.Equal(done, (await mediator.Send(new TaskUpdateCommand(Owner, task, null, null, done), CancellationToken.None)).Response!.StatusId);
    }
}
