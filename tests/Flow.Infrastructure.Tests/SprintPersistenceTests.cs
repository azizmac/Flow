using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Sprints.Commands.SprintCreateCommand;
using Flow.Application.Features.Sprints.Commands.SprintStartCommand;
using Flow.Application.Features.Sprints.Commands.TaskSetSprintCommand;
using Flow.Application.Features.Sprints.Queries.SprintReportQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Спринты на реальном Postgres (этап 2D): частичный unique-индекс «один активный», FK SetNull у задач, снимок
/// обязательств после перезагрузки, FQL `sprint` в SQL и burndown по журналу с настоящими временами.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SprintPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(Guid Board, Guid Task)> ArrangeAsync(string key)
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Спринты", key))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Задача", null, null)))!.Id;
        return (board.Id, task);
    }

    [Fact]
    public async Task Second_Active_Sprint_Hits_Partial_Unique_Index_And_Delete_Sets_Null()
    {
        var (board, task) = await ArrangeAsync("SPA");
        var first = (await db.SendAsync(new SprintCreateCommand(Owner, board)))!.Id;
        var second = (await db.SendAsync(new SprintCreateCommand(Owner, board)))!.Id;
        await db.SendAsync(new TaskSetSprintCommand(Owner, task, second));
        await db.SendAsync(new SprintStartCommand(Owner, first, Today, Today.AddDays(7)));

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.QueryAsync(ctx =>
            ctx.Database.ExecuteSqlAsync($"""UPDATE "Sprints" SET "State" = 1 WHERE "Id" = {second}""")));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);

        await db.QueryAsync(ctx => ctx.Database.ExecuteSqlAsync($"""DELETE FROM "Sprints" WHERE "Id" = {second}"""));
        Assert.Null(await db.QueryAsync(ctx => ctx.TaskItems.Where(t => t.Id == task).Select(t => t.SprintId).SingleAsync()));
    }

    [Fact]
    public async Task Fql_Sprint_Translates_To_Sql()
    {
        var (board, planned) = await ArrangeAsync("SPF");
        var backlog = (await db.SendAsync(new TaskCreateCommand(Owner, board, "В бэклоге", null, null)))!.Id;
        var sprint = (await db.SendAsync(new SprintCreateCommand(Owner, board, "Альфа-SPF")))!.Id;
        await db.SendAsync(new TaskSetSprintCommand(Owner, planned, sprint));

        async Task<IEnumerable<Guid>> Fql(string q) =>
            (await db.SendAsync(new TaskSearchQuery(Owner, board, Fql: q))).Items.Select(t => t.Id);

        Assert.Equal([planned], await Fql("sprint = \"Альфа-SPF\""));
        Assert.Equal([planned], await Fql("sprint in (openSprints())"));
        Assert.Equal([backlog], await Fql("sprint is EMPTY"));
    }

    [Fact]
    public async Task Burndown_Follows_Journal_By_Day()
    {
        var (board, task) = await ArrangeAsync("SPB");
        var done = (await db.QueryAsync(ctx => ctx.Statuses.Where(s => s.BoardId == board && s.IsFinal).Select(s => s.Id).FirstAsync()));
        var sprint = (await db.SendAsync(new SprintCreateCommand(Owner, board)))!.Id;
        await db.SendAsync(new TaskSetEstimateCommand(Owner, task, 5m, null));
        await db.SendAsync(new TaskSetSprintCommand(Owner, task, sprint));
        await db.SendAsync(new SprintStartCommand(Owner, sprint, Today.AddDays(-3), Today.AddDays(4)));
        await db.SendAsync(new TaskUpdateCommand(Owner, task, null, null, done));

        // Журнал — источник burndown, поэтому времена записей переписываем: оценка и планирование — до старта
        // спринта, закрытие — «вчера в полдень».
        var beforeStart = Today.AddDays(-4).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await db.QueryAsync(ctx => ctx.Database.ExecuteSqlAsync(
            $"""UPDATE "TaskActivities" SET "CreatedAt" = {beforeStart} WHERE "TaskId" = {task}"""));
        var yesterdayNoon = Today.AddDays(-1).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        await db.QueryAsync(ctx => ctx.Database.ExecuteSqlAsync(
            $"""UPDATE "TaskActivities" SET "CreatedAt" = {yesterdayNoon} WHERE "TaskId" = {task} AND "Type" = {(int)TaskActivityType.StatusChanged}"""));

        var report = (await db.SendAsync(new SprintReportQuery(Owner, sprint)))!;

        Assert.Equal((1, 5m), (report.Committed.Count, report.Committed.Points));
        // Сегодня — последний день с фактом, дальше до конца спринта только идеальная линия.
        Assert.Equal([5m, 5m, 0m, 0m, null, null, null, null], report.Burndown.Select(p => p.Remaining));
        Assert.Equal(0m, report.Burndown[^1].Ideal);
        Assert.Equal(Today.AddDays(-3), report.Burndown[0].Date);
        Assert.Equal(5m, report.Burndown[0].Ideal);
    }
}
