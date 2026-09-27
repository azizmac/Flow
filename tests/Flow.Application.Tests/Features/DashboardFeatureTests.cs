using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Dashboards;
using Flow.Application.Features.Filters;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Dashboards;
using MediatR;
using Xunit;
using BoardVisibility = Flow.Domain.Entities.BoardVisibility;
using DomainWidgetType = Flow.Domain.Entities.WidgetType;
using SharedWidgetType = Flow.Shared.Contracts.Dashboards.WidgetType;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Дашборды в Application (docs/TZ_task_views.md §8): дашборд по умолчанию, видимость, проверка настроек виджета,
/// данные виджетов правами смотрящего и ошибки виджета в ответе. Права — PermissionTests, SQL разбивки — Flow.Infrastructure.Tests.
/// </summary>
public class DashboardFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static Guid AddUser(FakeUserRepository users, UserRole role, string username, string first = "Имя")
    {
        var user = User.Create(username, $"{username}@example.com", first, "Фамилия");
        user.ChangeRole(role);
        user.MarkActive();
        users.Add(user);
        return user.Id;
    }

    private static async Task<BoardResponse> BoardAsync(IMediator mediator, string key = "PRJ") =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Проект " + key, key), CancellationToken.None)).Response!;

    private static async Task<Guid> TaskAsync(IMediator mediator, BoardResponse board, string title = "Задача") =>
        (await mediator.Send(new TaskCreateCommand(Owner, board.Id, title, null, null), CancellationToken.None))!.Id;

    private static async Task<(DashboardResponse Dashboard, Guid WidgetId)> WidgetAsync(
        IMediator mediator, DomainWidgetType type, WidgetConfig config, bool shared = false)
    {
        var dashboard = await mediator.Send(new DashboardCreateCommand(Owner, "Обзор", shared), CancellationToken.None);
        var updated = await mediator.Send(new WidgetAddCommand(Owner, dashboard.Id, type, null, config), CancellationToken.None);
        return (updated!, updated!.Widgets.Single().Id);
    }

    private static async Task<WidgetDataResponse> DataAsync(IMediator mediator, (DashboardResponse Dashboard, Guid WidgetId) widget, Guid? actor = null) =>
        (await mediator.Send(new WidgetDataQuery(actor ?? Owner, widget.Dashboard.Id, widget.WidgetId), CancellationToken.None))!;

    [Fact]
    public async Task First_Own_Dashboard_Is_Default_And_The_Flag_Moves()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        Assert.Null(await mediator.Send(new DashboardDefaultQuery(Owner), CancellationToken.None));

        var first = await mediator.Send(new DashboardCreateCommand(Owner, "Первый", false), CancellationToken.None);
        var second = await mediator.Send(new DashboardCreateCommand(Owner, "Второй", false), CancellationToken.None);
        Assert.True(first.IsDefault);
        Assert.False(second.IsDefault);

        await mediator.Send(new DashboardUpdateCommand(Owner, second.Id, IsDefault: true), CancellationToken.None);

        Assert.Equal(second.Id, (await mediator.Send(new DashboardDefaultQuery(Owner), CancellationToken.None))!.Id);
        Assert.False((await mediator.Send(new DashboardGetQuery(Owner, first.Id), CancellationToken.None))!.IsDefault);
    }

    [Fact]
    public async Task Private_Is_Invisible_Shared_Is_Visible_But_Not_Own()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var mine = await mediator.Send(new DashboardCreateCommand(Owner, "Личный", false), CancellationToken.None);
        var shared = await mediator.Send(new DashboardCreateCommand(Owner, "Общий", true), CancellationToken.None);

        var visible = await mediator.Send(new DashboardListQuery(member), CancellationToken.None);

        Assert.Equal([shared.Id], visible.Select(d => d.Id));
        Assert.False(visible.Single().IsOwn);
        Assert.Null(await mediator.Send(new DashboardGetQuery(member, mine.Id), CancellationToken.None));
        // Чужой общий дашборд не становится «по умолчанию» смотрящего.
        Assert.Null(await mediator.Send(new DashboardDefaultQuery(member), CancellationToken.None));
    }

    [Fact]
    public async Task Widget_Config_Is_Validated_And_Cleaned_By_Type()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var dashboard = await mediator.Send(new DashboardCreateCommand(Owner, "Обзор", false), CancellationToken.None);
        Task<DashboardResponse?> Add(DomainWidgetType type, WidgetConfig config) =>
            mediator.Send(new WidgetAddCommand(Owner, dashboard.Id, type, null, config), CancellationToken.None);

        await Assert.ThrowsAsync<FqlException>(() => Add(DomainWidgetType.Counter, new WidgetConfig(Fql: "status =")));
        await Assert.ThrowsAsync<ArgumentException>(() => Add(DomainWidgetType.Breakdown, new WidgetConfig(GroupBy: "color")));
        await Assert.ThrowsAsync<ArgumentException>(() => Add(DomainWidgetType.SprintBurndown, new WidgetConfig()));
        await Assert.ThrowsAsync<ArgumentException>(() => Add(DomainWidgetType.MilestoneProgress, new WidgetConfig()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Add(DomainWidgetType.TaskList, new WidgetConfig(FilterId: Guid.NewGuid())));

        var added = await Add(DomainWidgetType.TaskList, new WidgetConfig(Fql: "  priority >= high ", Limit: 500, GroupBy: "status", Text: "лишнее"));

        Assert.Equal(new WidgetConfig(Fql: "priority >= high", Limit: DashboardLimits.MaxLimit), added!.Widgets.Single().Config);
    }

    [Fact]
    public async Task Only_Author_Edits_Widgets()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");
        var (dashboard, widget) = await WidgetAsync(mediator, DomainWidgetType.Markdown, new WidgetConfig(Text: "Привет"), shared: true);

        await Assert.ThrowsAsync<Flow.Application.Exceptions.ForbiddenException>(() =>
            mediator.Send(new WidgetRemoveCommand(admin, dashboard.Id, widget), CancellationToken.None));
        Assert.Equal("Привет", (await DataAsync(mediator, (dashboard, widget), admin)).Text);
    }

    [Fact]
    public async Task Counter_And_List_Are_Counted_With_The_Viewer_Rights()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var open = await BoardAsync(mediator, "OPN");
        var closed = await BoardAsync(mediator, "PRV");
        await TaskAsync(mediator, open, "Видна");
        await TaskAsync(mediator, closed, "Скрыта 1");
        await TaskAsync(mediator, closed, "Скрыта 2");
        await mediator.Send(new BoardVisibilitySetCommand(Owner, closed.Id, BoardVisibility.Private), CancellationToken.None);

        var counter = await WidgetAsync(mediator, DomainWidgetType.Counter, new WidgetConfig(), shared: true);
        var list = await WidgetAsync(mediator, DomainWidgetType.TaskList, new WidgetConfig(Limit: 2), shared: true);

        Assert.Equal(3, (await DataAsync(mediator, counter)).Count);
        Assert.Equal(1, (await DataAsync(mediator, counter, member)).Count);
        var forMember = await DataAsync(mediator, list, member);
        Assert.Equal(["Видна"], forMember.Tasks!.Select(t => t.Title));
        Assert.Equal((2, 3), ((await DataAsync(mediator, list)).Tasks!.Count, (await DataAsync(mediator, list)).Count));
    }

    [Fact]
    public async Task Breakdown_By_Status_Has_Names_And_Folds_The_Tail_Into_Other()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var working = board.Statuses.Single(s => s.Name == "В работе").Id;
        await TaskAsync(mediator, board);
        var moved = await TaskAsync(mediator, board);
        await mediator.Send(new TaskUpdateCommand(Owner, moved, null, null, working), CancellationToken.None);

        var byStatus = await DataAsync(mediator, await WidgetAsync(mediator, DomainWidgetType.Breakdown, new WidgetConfig(GroupBy: "status")));
        Assert.Equal(["В работе", board.Statuses.Single(s => s.IsInitial).Name], byStatus.Groups!.Select(g => g.Label).Order());
        Assert.Equal(2, byStatus.Count);

        // Девять исполнителей → семь групп и «Другие».
        for (var i = 0; i < 9; i++)
        {
            var user = AddUser(users, UserRole.Member, $"user{i}", $"Человек{i}");
            var task = await TaskAsync(mediator, board);
            await mediator.Send(new TaskAssignCommand(Owner, task, user), CancellationToken.None);
        }

        var byAssignee = await DataAsync(mediator, await WidgetAsync(mediator, DomainWidgetType.Breakdown, new WidgetConfig(GroupBy: "assignee")));
        Assert.Equal(DashboardLimits.MaxGroups + 1, byAssignee.Groups!.Count);
        Assert.Equal("Не назначена", byAssignee.Groups[0].Label);
        Assert.Equal(("Другие", 11), (byAssignee.Groups[^1].Label, byAssignee.Groups.Sum(g => g.Count)));
    }

    [Fact]
    public async Task Created_Vs_Closed_Has_A_Point_Per_Day()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        await TaskAsync(mediator, board);
        var done = await TaskAsync(mediator, board);
        await mediator.Send(new TaskUpdateCommand(Owner, done, null, null, board.Statuses.Single(s => s.IsFinal).Id), CancellationToken.None);

        var data = await DataAsync(mediator, await WidgetAsync(mediator, DomainWidgetType.CreatedVsClosed, new WidgetConfig(Days: 7)));

        Assert.Equal(7, data.Days!.Count);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), data.Days[^1].Date);
        Assert.Equal((2, 1), (data.Days.Sum(d => d.Created), data.Days.Sum(d => d.Closed)));
    }

    [Fact]
    public async Task Widget_Errors_Come_Back_In_The_Response()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var filter = await mediator.Send(new SavedFilterCreateCommand(Owner, "Фильтр", "", false), CancellationToken.None);
        var byFilter = await WidgetAsync(mediator, DomainWidgetType.Counter, new WidgetConfig(FilterId: filter.Id));
        await mediator.Send(new SavedFilterDeleteCommand(Owner, filter.Id), CancellationToken.None);

        var burndown = await WidgetAsync(mediator, DomainWidgetType.SprintBurndown, new WidgetConfig(BoardId: board.Id));

        Assert.Contains("фильтр", (await DataAsync(mediator, byFilter)).Error);
        Assert.Contains("активного спринта", (await DataAsync(mediator, burndown)).Error);
        Assert.Equal(SharedWidgetType.SprintBurndown, (await DataAsync(mediator, burndown)).Type);
    }
}
