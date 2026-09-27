using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.StatusDeleteCommand;
using Flow.Application.Features.Boards.Workflow;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskChecklistCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Xunit;
using ProjectRole = Flow.Domain.Entities.ProjectRole;
using SharedMode = Flow.Shared.Contracts.Boards.WorkflowMode;
using SharedRole = Flow.Shared.Contracts.Boards.ProjectRole;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Workflow в Application (docs/TZ_workflow_config.md §2, этап 3B): проверка в смене статуса, контекст условий
/// (роль в проекте, подзадачи, чек-лист), список переходов задачи, права на правку графа. Граф и тупики — в Domain.
/// </summary>
public class WorkflowFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<(BoardResponse Board, Guid Todo, Guid Doing, Guid Review, Guid Done)> ArrangeAsync(IMediator mediator)
    {
        var board = (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "PRJ"), CancellationToken.None)).Response!;
        Guid Id(string name) => board.Statuses.Single(s => s.Name == name).Id;
        return (board, Id("Не начата"), Id("В работе"), Id("На проверке"), Id("Сделана"));
    }

    private static Guid AddUser(FakeUserRepository users, UserRole role, string username)
    {
        var user = User.Create(username, $"{username}@example.com", "Имя", "Фамилия");
        user.ChangeRole(role);
        user.MarkActive();
        users.Add(user);
        return user.Id;
    }

    private static Task<WorkflowResponse?> Set(IMediator mediator, Guid boardId, params TransitionRequest[] transitions) =>
        mediator.Send(new WorkflowSetCommand(Owner, boardId, SharedMode.Restricted, transitions), CancellationToken.None);

    [Fact]
    public async Task Status_Change_Outside_Graph_Is_Refused_With_Reasons_And_No_Journal()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var (board, todo, doing, review, done) = await ArrangeAsync(mediator);
        await Set(mediator, board.Id, new(todo, doing), new(doing, review), new(review, done), new(null, todo));
        var task = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Задача", null, null), CancellationToken.None))!.Id;

        var refused = await mediator.Send(new TaskUpdateCommand(Owner, task, "Новое название", null, done), CancellationToken.None);

        Assert.Contains("«Не начата» → «Сделана»", refused.Reasons!.Single());
        Assert.DoesNotContain(activities.ForTask(task), a => a.Type is TaskActivityType.StatusChanged or TaskActivityType.TitleChanged);

        Assert.NotNull((await mediator.Send(new TaskUpdateCommand(Owner, task, null, null, doing), CancellationToken.None)).Response);
    }

    [Fact]
    public async Task Conditions_Use_Project_Role_Assignee_Children_And_Checklist()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var developer = AddUser(users, UserRole.Member, "dev");
        var (board, todo, doing, review, done) = await ArrangeAsync(mediator);
        await mediator.Send(new BoardMemberSetCommand(Owner, board.Id, developer, ProjectRole.Developer), CancellationToken.None);
        await Set(mediator, board.Id,
            new(todo, doing, Conditions: new TransitionConditionsDto(RequireAssignee: true)),
            new(doing, done, Conditions: new TransitionConditionsDto(MinRole: SharedRole.Admin, RequireChecklistDone: true, RequireChildrenDone: true)),
            new(doing, review), new(review, todo));
        var task = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Задача", null, null), CancellationToken.None))!.Id;

        var transitions = (await mediator.Send(new TaskTransitionsQuery(developer, task), CancellationToken.None))!;
        var toDoing = transitions.Single(t => t.StatusId == doing);
        Assert.Equal(["Сначала назначьте исполнителя"], toDoing.Reasons);
        Assert.False(transitions.Single(t => t.StatusId == done).Allowed);

        await mediator.Send(new TaskAssignCommand(Owner, task, developer), CancellationToken.None);
        Assert.NotNull((await mediator.Send(new TaskUpdateCommand(developer, task, null, null, doing), CancellationToken.None)).Response);

        // Роль в проекте — Developer: переход только для администратора закрыт; чек-лист тоже не выполнен.
        await mediator.Send(new TaskChecklistAddCommand(Owner, task, "Пункт"), CancellationToken.None);
        var reasons = (await mediator.Send(new TaskUpdateCommand(developer, task, null, null, done), CancellationToken.None)).Reasons!;
        Assert.Equal(2, reasons.Count);

        // Admin проходит по роли, но чек-лист всё равно держит.
        Assert.Equal(["Чек-лист выполнен не полностью"], (await mediator.Send(new TaskUpdateCommand(Owner, task, null, null, done), CancellationToken.None)).Reasons);
    }

    [Fact]
    public async Task Free_Mode_And_Status_Delete_Are_Not_Checked()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (board, todo, doing, review, done) = await ArrangeAsync(mediator);
        var task = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Задача", null, null), CancellationToken.None))!.Id;

        Assert.NotNull((await mediator.Send(new TaskUpdateCommand(Owner, task, null, null, review), CancellationToken.None)).Response);

        // Служебный перенос при удалении статуса workflow не проходит — иначе статус не удалить.
        await Set(mediator, board.Id, new(todo, doing), new(doing, done), new(review, done));
        Assert.NotNull(await mediator.Send(new StatusDeleteCommand(Owner, board.Id, review, todo), CancellationToken.None));
    }

    [Fact]
    public async Task Workflow_Is_Read_By_Viewers_And_Changed_By_Project_Admins()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var developer = AddUser(users, UserRole.Developer, "dev");
        var (board, todo, doing, _, _) = await ArrangeAsync(mediator);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new WorkflowSetCommand(developer, board.Id, SharedMode.Free, [new(todo, doing)]), CancellationToken.None));

        var saved = (await mediator.Send(new WorkflowSetCommand(Owner, board.Id, SharedMode.Free, [new(todo, doing)]), CancellationToken.None))!;
        Assert.Equal(2, saved.DeadEnds.Count); // Free: тупики («В работе», «На проверке») допустимы, но показываются

        var read = (await mediator.Send(new WorkflowGetQuery(developer, board.Id), CancellationToken.None))!;
        Assert.Equal(SharedMode.Free, read.Mode);
        Assert.Single(read.Transitions);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Set(mediator, board.Id, new TransitionRequest(todo, doing)));
    }
}
