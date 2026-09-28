using Flow.Application.Exceptions;
using Flow.Application.Features.Attachments.Queries.AttachmentListQuery;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberRemoveCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Flow.Application.Features.Boards.Queries.BoardMembersQuery;
using Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Search.Queries.SimilarTasksQuery;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;
using Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using MediatR;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Приватные проекты (docs/TZ_project_access.md, этап 4B): скрытый проект для не участника — как несуществующий.
/// Каждый запрос чтения отвечает null/пусто (404), каждая команда — ProjectNotFoundException (404, а не 403).
/// SQL-фильтры (поиск, похожие, списки) — в Flow.Infrastructure.Tests.
/// </summary>
public class PrivateProjectTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static Guid AddUser(FakeUserRepository users, UserRole role, string username)
    {
        var user = User.Create(username, $"{username}@example.com", "Имя", "Фамилия");
        user.ChangeRole(role);
        user.MarkActive();
        users.Add(user);
        return user.Id;
    }

    /// <summary>Приватный проект с задачей и комментарием; открытый рядом — для сравнения.</summary>
    private static async Task<(Guid Private, Guid Open, Guid Task)> ArrangeAsync(IMediator mediator)
    {
        var hidden = (await mediator.Send(new BoardCreateCommand(Owner, "Секрет", "SEC"), CancellationToken.None)).Response!.Id;
        var open = (await mediator.Send(new BoardCreateCommand(Owner, "Открытый", "OPN"), CancellationToken.None)).Response!.Id;
        var task = (await mediator.Send(new TaskCreateCommand(Owner, hidden, "Скрытая задача", null, null), CancellationToken.None))!.Id;
        await mediator.Send(new TaskCreateCommand(Owner, open, "Открытая задача", null, null), CancellationToken.None);
        await mediator.Send(new TaskCommentAddCommand(Owner, task, "Комментарий"), CancellationToken.None);
        await mediator.Send(new BoardVisibilitySetCommand(Owner, hidden, BoardVisibility.Private), CancellationToken.None);
        return (hidden, open, task);
    }

    [Fact]
    public async Task Hidden_Project_Reads_Like_Missing_For_Non_Member()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var developer = AddUser(users, UserRole.Developer, "dev");
        var (hidden, open, task) = await ArrangeAsync(mediator);

        var boards = await mediator.Send(new BoardListQuery(developer), CancellationToken.None);
        Assert.Equal([open], boards.Select(b => b.Id));
        Assert.Null(await mediator.Send(new BoardGetQuery(developer, hidden), CancellationToken.None));
        Assert.Empty(await mediator.Send(new TaskListQuery(developer, hidden), CancellationToken.None));
        Assert.Null(await mediator.Send(new TaskGetQuery(developer, task), CancellationToken.None));
        Assert.Null(await mediator.Send(new TaskCommentListQuery(developer, task), CancellationToken.None));
        Assert.Null(await mediator.Send(new TaskActivityListQuery(developer, task), CancellationToken.None));
        Assert.Null(await mediator.Send(new AttachmentListQuery(developer, task), CancellationToken.None));
        Assert.Null(await mediator.Send(new BoardMembersQuery(developer, hidden), CancellationToken.None));

        var all = await mediator.Send(new TaskSearchQuery(developer, Limit: 500), CancellationToken.None);
        Assert.DoesNotContain(all.Items, t => t.Id == task);
        Assert.Equal(1, all.Total);

        var access = await mediator.Send(new BoardMyAccessQuery(developer), CancellationToken.None);
        Assert.Equal([open], access.Select(a => a.BoardId));
    }

    [Fact]
    public async Task Hidden_Project_Commands_Answer_NotFound_Not_Forbidden()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var developer = AddUser(users, UserRole.Developer, "dev");
        var (hidden, _, task) = await ArrangeAsync(mediator);

        Task NotFound(Func<Task> action) => Assert.ThrowsAsync<ProjectNotFoundException>(action);

        await NotFound(() => mediator.Send(new TaskCreateCommand(developer, hidden, "T", null, null), CancellationToken.None));
        await NotFound(() => mediator.Send(new TaskUpdateCommand(developer, task, "T", null, null), CancellationToken.None));
        await NotFound(() => mediator.Send(new TaskAssignCommand(developer, task, developer), CancellationToken.None));
        await NotFound(() => mediator.Send(new TaskDeleteCommand(developer, task), CancellationToken.None));
        await NotFound(() => mediator.Send(new TaskCommentAddCommand(developer, task, "Текст"), CancellationToken.None));
        await NotFound(() => mediator.Send(new BoardRenameCommand(developer, hidden, "X"), CancellationToken.None));
        await NotFound(() => mediator.Send(new BoardVisibilitySetCommand(developer, hidden, BoardVisibility.Open), CancellationToken.None));
    }

    [Fact]
    public async Task Member_And_Global_Admin_See_Private_Project()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Reader, "member");
        var admin = AddUser(users, UserRole.Admin, "admin");
        var (hidden, _, task) = await ArrangeAsync(mediator);
        await mediator.Send(new BoardMemberSetCommand(Owner, hidden, member, ProjectRole.Member), CancellationToken.None);

        Assert.NotNull(await mediator.Send(new TaskGetQuery(member, task), CancellationToken.None));
        Assert.NotNull(await mediator.Send(new TaskGetQuery(admin, task), CancellationToken.None));
        Assert.Contains(await mediator.Send(new BoardListQuery(member), CancellationToken.None), b => b.Id == hidden);

        // Участник с ролью Member создаёт задачи и в приватном проекте, роль по умолчанию тут не действует.
        Assert.NotNull(await mediator.Send(new TaskCreateCommand(member, hidden, "Своя", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Last_Admin_Of_Private_Project_Cannot_Be_Demoted_Or_Removed()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var lead = AddUser(users, UserRole.Member, "lead");
        var second = AddUser(users, UserRole.Member, "second");
        var (hidden, open, _) = await ArrangeAsync(mediator);
        await mediator.Send(new BoardMemberSetCommand(Owner, hidden, lead, ProjectRole.Admin), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new BoardMemberSetCommand(Owner, hidden, lead, ProjectRole.Developer), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new BoardMemberRemoveCommand(Owner, hidden, lead), CancellationToken.None));

        // Второй администратор снимает ограничение; в открытом проекте его нет вовсе.
        await mediator.Send(new BoardMemberSetCommand(Owner, hidden, second, ProjectRole.Admin), CancellationToken.None);
        Assert.Null((await mediator.Send(new BoardMemberRemoveCommand(Owner, hidden, lead), CancellationToken.None)).ValidationError);

        await mediator.Send(new BoardMemberSetCommand(Owner, open, lead, ProjectRole.Admin), CancellationToken.None);
        Assert.Null((await mediator.Send(new BoardMemberRemoveCommand(Owner, open, lead), CancellationToken.None)).ValidationError);
    }

    [Fact]
    public async Task Search_Passes_Visible_Boards_And_Hides_Direct_Hit_And_Project_Key()
    {
        var context = TestMediatorFactory.CreateSearchContext();
        var developer = AddUser(context.Users, UserRole.Developer, "dev");
        var (hidden, open, _) = await ArrangeAsync(context.Mediator);

        var byCode = await context.Mediator.Send(
            new SearchQuery(developer, "SEC-1", null, null, false, SearchMode.Hybrid, 20, 0), CancellationToken.None);
        Assert.Null(byCode!.Intent.TaskId);
        Assert.Equal([open], context.Index.LastCriteria!.VisibleBoardIds!.ToArray());

        await context.Mediator.Send(new SearchQuery(developer, "проект:SEC экспорт", null, null, false, SearchMode.Hybrid, 20, 0), CancellationToken.None);
        Assert.Null(context.Index.LastCriteria!.BoardId);
        Assert.Contains("SEC", context.Index.LastCriteria.Query);

        // Глобальный Owner видит всё — фильтр не нужен вовсе.
        await context.Mediator.Send(new SearchQuery(Owner, "экспорт", null, null, false, SearchMode.Hybrid, 20, 0), CancellationToken.None);
        Assert.Null(context.Index.LastCriteria!.VisibleBoardIds);
        Assert.NotEqual(Guid.Empty, hidden);
    }

    [Fact]
    public async Task Similar_For_Hidden_Task_Is_NotFound()
    {
        var context = TestMediatorFactory.CreateSearchContext();
        var developer = AddUser(context.Users, UserRole.Developer, "dev");
        var (_, _, task) = await ArrangeAsync(context.Mediator);

        Assert.Null(await context.Mediator.Send(new SimilarTasksQuery(developer, task, 5), CancellationToken.None));
        Assert.NotNull(await context.Mediator.Send(new SimilarTasksQuery(Owner, task, 5), CancellationToken.None));
    }

    [Fact]
    public async Task Without_Private_Projects_There_Is_No_Filter()
    {
        var context = TestMediatorFactory.CreateSearchContext();
        var developer = AddUser(context.Users, UserRole.Developer, "dev");
        await context.Mediator.Send(new BoardCreateCommand(Owner, "Открытый", "OPN"), CancellationToken.None);

        await context.Mediator.Send(new SearchQuery(developer, "экспорт", null, null, false, SearchMode.Hybrid, 20, 0), CancellationToken.None);

        Assert.Null(context.Index.LastCriteria!.VisibleBoardIds);
    }
}
