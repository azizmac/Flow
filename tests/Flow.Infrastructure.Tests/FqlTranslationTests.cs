using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Filters;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskLinkCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetScheduleCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainPriority = Flow.Domain.Entities.TaskPriority;
using TaskLinkType = Flow.Domain.Entities.TaskLinkType;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// FQL → SQL на реальном Postgres (docs/TZ_task_views.md §7, этап 2A): каждое поле и оператор TaskFilterTranslator,
/// NULL-семантика отрицаний, подзапросы видов и связей, ORDER BY и счётчики с условием. Один проект на все случаи —
/// выдача сужается FQL-ом «project = FQL».
/// </summary>
[Collection(PostgresCollection.Name)]
public class FqlTranslationTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;
    private BoardResponse _board = null!;

    /// <summary>Свой проект на каждый тест (фикстура общая), в FQL подставляется вместо «FQL».</summary>
    private readonly string _key = "Q" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    private readonly Dictionary<string, Guid> _t = [];

    public async Task InitializeAsync()
    {
        _board = (await db.SendAsync(new BoardCreateCommand(Owner, "Fql", _key))).Response!;
        var type = (TaskTypeKind kind) => _board.TaskTypes.First(t => t.Kind == kind).Id;

        async Task<Guid> Create(string name, TaskTypeKind kind, DomainPriority priority, Guid? parent = null) =>
            _t[name] = (await db.SendAsync(new TaskCreateCommand(Owner, _board.Id, $"{name} задача", null, null, type(kind), priority, parent)))!.Id;

        var epic = await Create("epic", TaskTypeKind.Epic, DomainPriority.Critical);
        var story = await Create("story", TaskTypeKind.Story, DomainPriority.High, epic);
        await Create("sub", TaskTypeKind.Task, DomainPriority.Low, story);
        var bug = await Create("bug", TaskTypeKind.Bug, DomainPriority.Medium);
        await Create("lone", TaskTypeKind.Task, DomainPriority.None);

        await db.SendAsync(new TaskAssignCommand(Owner, bug, Owner));
        await db.SendAsync(new TaskUpdateCommand(Owner, story, null, "Описание про оплату", _board.Statuses.Single(s => s.IsFinal).Id));
        await db.SendAsync(new TaskSetScheduleCommand(Owner, bug, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)));
        await db.SendAsync(new TaskSetEstimateCommand(Owner, bug, 3m, 150));
        await db.SendAsync(new TaskLinkCreateCommand(Owner, bug, TaskLinkType.Blocks, _t["lone"]));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string[]> Find(string fql)
    {
        var page = await db.SendAsync(new TaskSearchQuery(Owner, Offset: 0, Limit: 100, Fql: $"project = FQL AND ({fql})".Replace("FQL", _key)));
        Assert.Equal(page.Items.Count, page.Matched);
        return page.Items.Select(i => _t.Single(p => p.Value == i.Id).Key).Order().ToArray();
    }

    [Theory]
    [InlineData("typeKind = epic", "epic")]
    [InlineData("typeKind in (task, bug)", "bug,lone,sub")]
    [InlineData("statusCategory = done", "story")]
    [InlineData("statusCategory != done", "bug,epic,lone,sub")]
    [InlineData("status = \"Не начата\"", "bug,epic,lone,sub")]
    [InlineData("priority >= high", "epic,story")]
    [InlineData("priority in (none, low)", "lone,sub")]
    [InlineData("assignee = me()", "bug")]
    [InlineData("assignee != me()", "epic,lone,story,sub")]
    [InlineData("assignee IS EMPTY", "epic,lone,story,sub")]
    [InlineData("parent = FQL-1", "story")]
    [InlineData("parent = childrenOf(FQL-1)", "story,sub")]
    [InlineData("parent IS EMPTY", "bug,epic,lone")]
    [InlineData("due < 2026-09-11 AND start >= 2026-09-01", "bug")]
    [InlineData("due IS NOT EMPTY", "bug")]
    [InlineData("points >= 3 AND estimate > 2h", "bug")]
    [InlineData("estimate IS EMPTY", "epic,lone,story,sub")]
    [InlineData("text ~ оплат", "story")]
    [InlineData("text ~ \"FQL-5\"", "lone")]
    [InlineData("linked = isBlocked()", "lone")]
    [InlineData("linked = blockedBy(FQL-4)", "lone")]
    [InlineData("linked = linkedTo(FQL-5)", "bug")]
    [InlineData("linked IS EMPTY", "epic,story,sub")]
    [InlineData("NOT typeKind = task OR priority = low", "bug,epic,story,sub")]
    [InlineData("created >= -1d AND updated <= today()", "bug,epic,lone,story,sub")]
    [InlineData("created < 2020-01-01", "")]
    public async Task Fql_Should_Translate_To_Sql(string fql, string expected)
    {
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), await Find(fql));
    }

    [Fact]
    public async Task Order_By_Should_Use_Several_Keys()
    {
        var ordered = await db.SendAsync(new TaskSearchQuery(Owner, Offset: 0, Fql: $"project = {_key} ORDER BY priority DESC, key"));
        Assert.Equal(["epic", "story", "bug", "sub", "lone"], ordered.Items.Select(i => _t.Single(p => p.Value == i.Id).Key));
    }

    [Fact]
    public async Task Saved_Filter_Should_Persist_With_Stars()
    {
        var filter = await db.SendAsync(new SavedFilterCreateCommand(Owner, "Критичные", $"project = {_key} AND priority = critical", true));
        await db.SendAsync(new SavedFilterStarCommand(Owner, filter.Id, true));

        var list = await db.SendAsync(new SavedFilterListQuery(Owner));
        Assert.True(list.Single(f => f.Id == filter.Id).IsStarred);

        Assert.True(await db.SendAsync(new SavedFilterDeleteCommand(Owner, filter.Id)));
        Assert.Equal(0, await db.QueryAsync(ctx => ctx.SavedFilterStars.CountAsync(s => s.FilterId == filter.Id)));
    }
}
