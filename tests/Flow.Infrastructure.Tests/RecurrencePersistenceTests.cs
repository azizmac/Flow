using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Recurrence;
using Flow.Shared.Contracts.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Повторения на реальном Postgres (этап 1F): правило-complex type переживает перезагрузку, вхождения держат
/// идемпотентность, удалённая копия оставляет вхождение (TaskId → null), удаление образца уносит правило каскадом.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RecurrencePersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Rule_Round_Trips_And_Generation_Is_Idempotent()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Регулярные", "RECP"))).Response!;
        var template = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Планёрка", null, null)))!;
        var today = new DateOnly(2026, 9, 21);
        var saved = (await db.SendAsync(new TaskRecurrenceSetCommand(Owner, template.Id,
            new TaskRecurrenceRequest(RecurrenceFrequency.Weekly, 1, today.AddDays(-7), [DayOfWeek.Monday, DayOfWeek.Wednesday], DueOffsetDays: 0))))!;
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], saved.WeekDays);

        var reloaded = (await db.SendAsync(new TaskRecurrenceGetQuery(Owner, template.Id)))!;
        Assert.Equal((RecurrenceFrequency.Weekly, 2), (reloaded.Frequency, reloaded.WeekDays.Count));

        var ruleId = await db.QueryAsync(ctx => ctx.TaskRecurrences.Where(r => r.TemplateTaskId == template.Id).Select(r => r.Id).SingleAsync());
        // Понедельник и среда прошлой недели и понедельник этой.
        Assert.Equal(3, await db.SendAsync(new RecurrenceGenerateCommand(ruleId, today)));
        Assert.Equal(0, await db.SendAsync(new RecurrenceGenerateCommand(ruleId, today)));

        var copies = await db.QueryAsync(ctx => ctx.TaskItems.Where(t => t.BoardId == board.Id && t.Id != template.Id).OrderBy(t => t.DueDate).ToListAsync());
        Assert.Equal([today.AddDays(-7), today.AddDays(-5), today], copies.Select(c => c.DueDate!.Value));

        await db.SendAsync(new TaskDeleteCommand(Owner, copies[0].Id));
        Assert.True(await db.QueryAsync(ctx => ctx.TaskRecurrenceOccurrences.AnyAsync(o => o.RecurrenceId == ruleId && o.TaskId == null)));

        await db.SendAsync(new TaskDeleteCommand(Owner, template.Id));
        Assert.False(await db.QueryAsync(ctx => ctx.TaskRecurrences.AnyAsync(r => r.Id == ruleId)));
        Assert.False(await db.QueryAsync(ctx => ctx.TaskRecurrenceOccurrences.AnyAsync(o => o.RecurrenceId == ruleId)));
    }
}
