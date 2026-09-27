using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.StatusCreateCommand;
using Flow.Application.Features.Boards.Commands.StatusDeleteCommand;
using Flow.Application.Features.Boards.Commands.StatusReorderCommand;
using Flow.Application.Features.Boards.Commands.StatusUpdateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using MediatR;
using Xunit;
using DomainStatusType = Flow.Domain.Entities.StatusType;
using ProjectRole = Flow.Domain.Entities.ProjectRole;
using SharedStatusType = Flow.Shared.Contracts.Boards.StatusType;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Управление статусами (docs/TZ_workflow_config.md §1, этап 3A): права, перенос задач с журналом и переиндексацией.
/// Инварианты списка статусов (начальный, последний финальный, имена, порядок) — в Flow.Domain.Tests.
/// </summary>
public class StatusFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<BoardResponse> CreateBoardAsync(IMediator mediator) =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "PRJ"), CancellationToken.None)).Response!;

    private static Guid StatusOf(BoardResponse board, string name) => board.Statuses.Single(s => s.Name == name).Id;

    [Fact]
    public async Task Create_Appends_Status_With_Type()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);

        var response = await mediator.Send(
            new StatusCreateCommand(Owner, board.Id, "Отменена", DomainStatusType.Done, IsFinal: true), CancellationToken.None);

        var added = response!.Statuses[^1];
        Assert.Equal("Отменена", added.Name);
        Assert.Equal(SharedStatusType.Done, added.Type);
        Assert.True(added.IsFinal);
        Assert.Equal(2, response.Statuses.Count(s => s.IsFinal));
    }

    [Fact]
    public async Task Missing_Board_Returns_Null()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        Assert.Null(await mediator.Send(new StatusCreateCommand(Owner, Guid.NewGuid(), "X", null), CancellationToken.None));
    }

    [Fact]
    public async Task Update_Renames_Moves_Initial_And_Clears_Type()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);
        var inWork = StatusOf(board, "В работе");

        var response = await mediator.Send(
            new StatusUpdateCommand(Owner, board.Id, inWork, Name: "В разработке", IsInitial: true, ClearType: true), CancellationToken.None);

        var status = response!.Statuses.Single(s => s.Id == inWork);
        Assert.Equal("В разработке", status.Name);
        Assert.True(status.IsInitial);
        Assert.Null(status.Type);
        Assert.Single(response.Statuses, s => s.IsInitial);
    }

    [Fact]
    public async Task Update_Rejects_Clearing_Initial_Flag()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);

        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(
            new StatusUpdateCommand(Owner, board.Id, StatusOf(board, "Не начата"), IsInitial: false), CancellationToken.None));
    }

    [Fact]
    public async Task Changing_Final_Flag_Reindexes_Tasks_Of_That_Status_Only()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var board = await CreateBoardAsync(context.Mediator);
        var review = StatusOf(board, "На проверке");
        var inReview = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "На ревью", null, review), CancellationToken.None))!.Id;
        var other = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Другая", null, null), CancellationToken.None))!.Id;
        context.SearchIndex.Clear();

        await context.Mediator.Send(new StatusUpdateCommand(Owner, board.Id, review, Name: "Проверка"), CancellationToken.None);
        Assert.Empty(context.SearchIndex.All);

        await context.Mediator.Send(new StatusUpdateCommand(Owner, board.Id, review, IsFinal: true), CancellationToken.None);
        Assert.Single(context.SearchIndex.For(SearchSourceType.Task, inReview));
        Assert.Empty(context.SearchIndex.For(SearchSourceType.Task, other));
    }

    [Fact]
    public async Task Delete_Moves_Tasks_Writes_Journal_And_Reindexes()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var board = await CreateBoardAsync(context.Mediator);
        var review = StatusOf(board, "На проверке");
        var done = StatusOf(board, "Сделана");
        var moved = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "На ревью", null, review), CancellationToken.None))!.Id;
        var untouched = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Новая", null, null), CancellationToken.None))!.Id;
        context.SearchIndex.Clear();

        var response = await context.Mediator.Send(new StatusDeleteCommand(Owner, board.Id, review, done), CancellationToken.None);

        Assert.DoesNotContain(response!.Statuses, s => s.Id == review);
        Assert.Equal(done, (await context.Tasks.GetByIdAsync(moved, CancellationToken.None))!.StatusId);

        var entry = context.Activities.ForTask(moved).Single(a => a.Type == TaskActivityType.StatusChanged);
        Assert.Equal(Owner, entry.ActorId);
        Assert.Equal(review.ToString(), entry.OldValue);
        Assert.Equal(done.ToString(), entry.NewValue);
        Assert.DoesNotContain(context.Activities.ForTask(untouched), a => a.Type == TaskActivityType.StatusChanged);

        Assert.Single(context.SearchIndex.For(SearchSourceType.Task, moved));
        Assert.Empty(context.SearchIndex.For(SearchSourceType.Task, untouched));
    }

    [Fact]
    public async Task Delete_Rejects_Initial_Status_Without_Moving_Anything()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var board = await CreateBoardAsync(context.Mediator);
        var initial = StatusOf(board, "Не начата");
        var task = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Новая", null, null), CancellationToken.None))!.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Mediator.Send(
            new StatusDeleteCommand(Owner, board.Id, initial, StatusOf(board, "В работе")), CancellationToken.None));

        Assert.Equal(initial, (await context.Tasks.GetByIdAsync(task, CancellationToken.None))!.StatusId);
        Assert.DoesNotContain(context.Activities.ForTask(task), a => a.Type == TaskActivityType.StatusChanged);
    }

    [Fact]
    public async Task Reorder_Returns_Statuses_In_New_Order()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);
        var order = board.Statuses.Select(s => s.Id).Reverse().ToList();

        var response = await mediator.Send(new StatusReorderCommand(Owner, board.Id, order), CancellationToken.None);

        Assert.Equal(order, response!.Statuses.Select(s => s.Id));
    }

    [Fact]
    public async Task Project_Developer_Cannot_Manage_Statuses_But_Project_Admin_Can()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var user = User.Create("lead", "lead@example.com", "Имя", "Фамилия");
        user.ChangeRole(UserRole.Developer);
        user.MarkActive();
        users.Add(user);
        var board = await CreateBoardAsync(mediator);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new StatusCreateCommand(user.Id, board.Id, "Блок", null), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new StatusReorderCommand(user.Id, board.Id, board.Statuses.Select(s => s.Id).ToList()), CancellationToken.None));

        await mediator.Send(new BoardMemberSetCommand(Owner, board.Id, user.Id, ProjectRole.Admin), CancellationToken.None);
        Assert.NotNull(await mediator.Send(new StatusCreateCommand(user.Id, board.Id, "Блок", null), CancellationToken.None));
    }
}
