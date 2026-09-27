using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Filters;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>Сохранённые фильтры и подсказки FQL (docs/TZ_task_views.md §7, этап 2A). Трансляция в SQL — в Infrastructure.</summary>
public class SavedFilterFeatureTests
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

    [Fact]
    public async Task Filter_Query_Is_Validated_On_Save()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        await mediator.Send(new BoardCreateCommand(Owner, "Веб", "WEB"), CancellationToken.None);

        var saved = await mediator.Send(new SavedFilterCreateCommand(Owner, " Мои ", "project = WEB AND assignee = me()", false), CancellationToken.None);
        Assert.Equal("Мои", saved.Name);
        Assert.True(saved.IsOwner);

        var error = await Assert.ThrowsAsync<FqlException>(() =>
            mediator.Send(new SavedFilterCreateCommand(Owner, "Битый", "project = NOPE", false), CancellationToken.None));
        Assert.Equal(10, error.Position);

        await Assert.ThrowsAsync<FqlException>(() =>
            mediator.Send(new SavedFilterUpdateCommand(Owner, saved.Id, null, "status =", null), CancellationToken.None));
    }

    [Fact]
    public async Task Private_Is_Invisible_Shared_Is_Read_Only_For_Others()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var admin = AddUser(users, UserRole.Admin, "admin");
        var mine = await mediator.Send(new SavedFilterCreateCommand(Owner, "Личный", "priority >= high", false), CancellationToken.None);
        var shared = await mediator.Send(new SavedFilterCreateCommand(Owner, "Общий", "priority >= high", true), CancellationToken.None);

        var visible = await mediator.Send(new SavedFilterListQuery(member), CancellationToken.None);
        Assert.Equal([shared.Id], visible.Select(f => f.Id));
        Assert.Null(await mediator.Send(new SavedFilterGetQuery(member, mine.Id), CancellationToken.None));
        Assert.False(await mediator.Send(new SavedFilterDeleteCommand(member, mine.Id), CancellationToken.None));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new SavedFilterUpdateCommand(member, shared.Id, "Моё", null, null), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new SavedFilterDeleteCommand(member, shared.Id), CancellationToken.None));

        // Чужой общий фильтр Admin может убрать.
        Assert.True(await mediator.Send(new SavedFilterDeleteCommand(admin, shared.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Stars_Are_Per_User()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var shared = await mediator.Send(new SavedFilterCreateCommand(Owner, "Общий", "", true), CancellationToken.None);

        Assert.True((await mediator.Send(new SavedFilterStarCommand(member, shared.Id, true), CancellationToken.None))!.IsStarred);
        Assert.True((await mediator.Send(new SavedFilterStarCommand(member, shared.Id, true), CancellationToken.None))!.IsStarred);

        Assert.True((await mediator.Send(new SavedFilterListQuery(member), CancellationToken.None)).Single().IsStarred);
        Assert.False((await mediator.Send(new SavedFilterListQuery(Owner), CancellationToken.None)).Single().IsStarred);

        await mediator.Send(new SavedFilterStarCommand(member, shared.Id, false), CancellationToken.None);
        Assert.False((await mediator.Send(new SavedFilterListQuery(member), CancellationToken.None)).Single().IsStarred);
    }

    [Fact]
    public async Task Task_Search_Reports_Fql_Errors()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        var error = await Assert.ThrowsAsync<FqlException>(() =>
            mediator.Send(new TaskSearchQuery(Owner, Fql: "assignee = @ghost"), CancellationToken.None));

        Assert.Equal(11, error.Position);
    }

    [Theory]
    [InlineData("", FqlSlot.Field, null, "")]
    [InlineData("sta", FqlSlot.Field, null, "sta")]
    [InlineData("status ", FqlSlot.Operator, "status", "")]
    [InlineData("status = ", FqlSlot.Value, "status", "")]
    [InlineData("status = Сде", FqlSlot.Value, "status", "Сде")]
    [InlineData("status IN (a, ", FqlSlot.Value, "status", "")]
    [InlineData("assignee = \"Ива", FqlSlot.Value, "assignee", "Ива")]
    [InlineData("status = a ", FqlSlot.Connector, null, "")]
    [InlineData("status = a AND (pri", FqlSlot.Field, null, "pri")]
    [InlineData("due IS ", FqlSlot.Operator, "due", "")]
    [InlineData("status = a ORDER BY ", FqlSlot.OrderField, null, "")]
    [InlineData("status = a ORDER BY due ", FqlSlot.OrderNext, null, "")]
    public void Cursor_Is_Analyzed_From_Tokens_Before_It(string query, FqlSlot slot, string? field, string partial)
    {
        var cursor = FqlSuggester.Analyze(query, query.Length);

        Assert.Equal((slot, field, partial), (cursor.Slot, cursor.Field, cursor.Partial));
        Assert.Equal(query.Length, cursor.ReplaceFrom + cursor.ReplaceLength);
    }

    [Fact]
    public async Task Suggest_Offers_Values_Of_The_Field()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        await mediator.Send(new BoardCreateCommand(Owner, "Веб", "WEB"), CancellationToken.None);

        var statuses = await mediator.Send(new FqlSuggestQuery(Owner, "status = в", 10), CancellationToken.None);
        Assert.Equal("\"В работе\"", statuses.Items.First().Insert);
        Assert.Equal((9, 1), (statuses.ReplaceFrom, statuses.ReplaceLength));

        var projects = await mediator.Send(new FqlSuggestQuery(Owner, "project = ", 10), CancellationToken.None);
        Assert.Equal(["WEB"], projects.Items.Select(i => i.Insert));

        var fields = await mediator.Send(new FqlSuggestQuery(Owner, "pr", 2), CancellationToken.None);
        Assert.Equal(["priority", "project"], fields.Items.Select(i => i.Label).Order());
    }
}
