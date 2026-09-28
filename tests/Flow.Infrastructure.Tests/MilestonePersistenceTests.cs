using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Milestones;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Вехи на реальном Postgres (этап 2E): прогресс одним GROUP BY с join статусов (StatusChangedAt ставит UnitOfWork),
/// FQL `milestone` в SQL, FK SetNull у задач и каскад при удалении проекта.
/// </summary>
[Collection(PostgresCollection.Name)]
public class MilestonePersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(Guid Board, Guid Milestone, Guid Task)> ArrangeAsync(string key)
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Вехи", key))).Response!;
        var milestone = (await db.SendAsync(new MilestoneCreateCommand(Owner, board.Id, "Релиз-" + key)))!.Id;
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Задача", null, null)))!.Id;
        await db.SendAsync(new TaskSetMilestoneCommand(Owner, task, milestone));
        return (board.Id, milestone, task);
    }

    [Fact]
    public async Task Progress_Is_Counted_In_Sql()
    {
        var (board, milestone, done) = await ArrangeAsync("MSP");
        var late = (await db.SendAsync(new TaskCreateCommand(Owner, board, "Просрочена", null, null)))!.Id;
        await db.SendAsync(new TaskSetMilestoneCommand(Owner, late, milestone));
        await db.SendAsync(new TaskSetEstimateCommand(Owner, done, 3m, null));
        await db.SendAsync(new TaskSetEstimateCommand(Owner, late, 2m, null));
        await db.SendAsync(new TaskSetDueDateCommand(Owner, late, Today.AddDays(-2)));
        var final = await db.QueryAsync(ctx => ctx.Statuses.Where(s => s.BoardId == board && s.IsFinal).Select(s => s.Id).FirstAsync());
        await db.SendAsync(new TaskUpdateCommand(Owner, done, null, null, final));

        var progress = (await db.SendAsync(new MilestoneGetQuery(Owner, milestone)))!.Progress;

        Assert.Equal((2, 1, 5m, 3m, 1, 1), (progress.Total, progress.Done, progress.Points, progress.DonePoints, progress.Overdue, progress.ClosedRecently));
        Assert.Equal(Today.AddDays(14), progress.Forecast);
    }

    [Fact]
    public async Task Fql_Milestone_Translates_To_Sql()
    {
        var (board, _, inside) = await ArrangeAsync("MSF");
        var outside = (await db.SendAsync(new TaskCreateCommand(Owner, board, "Без вехи", null, null)))!.Id;

        async Task<IEnumerable<Guid>> Fql(string q) =>
            (await db.SendAsync(new TaskSearchQuery(Owner, board, Fql: q))).Items.Select(t => t.Id);

        Assert.Equal([inside], await Fql("milestone = \"релиз-msf\""));
        Assert.Equal([inside], await Fql("milestone in (openMilestones())"));
        Assert.Equal([outside], await Fql("milestone is EMPTY"));
    }

    [Fact]
    public async Task Deleting_Milestone_Row_Sets_Null_And_Board_Delete_Cascades()
    {
        var (board, milestone, task) = await ArrangeAsync("MSD");

        await db.QueryAsync(ctx => ctx.Database.ExecuteSqlAsync($"""DELETE FROM "Milestones" WHERE "Id" = {milestone}"""));
        Assert.Null(await db.QueryAsync(ctx => ctx.TaskItems.Where(t => t.Id == task).Select(t => t.MilestoneId).SingleAsync()));

        var second = (await db.SendAsync(new MilestoneCreateCommand(Owner, board, "Ещё одна")))!.Id;
        await db.SendAsync(new TaskSetMilestoneCommand(Owner, task, second));
        Assert.True(await db.SendAsync(new BoardDeleteCommand(Owner, board)));
        Assert.False(await db.QueryAsync(ctx => ctx.Milestones.AnyAsync(m => m.BoardId == board)));
    }

    /// <summary>Общая веха (этап 2H): uuid[] SharedBoardIds находит её в списке второго проекта, FQL `milestone` — задачи обоих.</summary>
    [Fact]
    public async Task Shared_Milestone_Is_Listed_And_Queried_Across_Projects()
    {
        var (front, milestone, frontTask) = await ArrangeAsync("SHF");
        var back = (await db.SendAsync(new BoardCreateCommand(Owner, "Бэк", "SHB"))).Response!;
        var backTask = (await db.SendAsync(new TaskCreateCommand(Owner, back.Id, "API", null, null)))!.Id;

        await db.SendAsync(new MilestoneShareCommand(Owner, milestone, [back.Id]));
        Assert.NotNull((await db.SendAsync(new TaskSetMilestoneCommand(Owner, backTask, milestone))).Response);

        var listed = Assert.Single((await db.SendAsync(new MilestoneListQuery(Owner, back.Id)))!);
        Assert.Equal((milestone, 2), (listed.Id, listed.Progress.Total));
        var found = await db.SendAsync(new TaskSearchQuery(Owner, Offset: 0, Limit: 50, Fql: "milestone = \"Релиз-SHF\""));
        Assert.Equal(new[] { frontTask, backTask }.Order(), found.Items.Select(t => t.Id).Order());
    }
}
