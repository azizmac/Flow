using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetScheduleCommand;
using Flow.Application.Features.Tasks.Queries.TaskCalendarQuery;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Календарь в SQL (этап 2F): отрезок задачи [начало ?? срок, срок ?? начало] пересекает окно — со сроком, без
/// начала, без срока и отрезком «через всё окно»; задачи без дат и за окном не попадают.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CalendarPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Window_Takes_Tasks_Whose_Span_Intersects_It()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Календарь", "CAL"))).Response!;
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 31);

        async Task<Guid> Task(string title, DateOnly? start, DateOnly? due)
        {
            var id = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, title, null, null)))!.Id;
            if (start is not null || due is not null)
                await db.SendAsync(new TaskSetScheduleCommand(Owner, id, start, due));
            return id;
        }

        var dueInside = await Task("Срок внутри", null, new DateOnly(2026, 10, 10));
        var startInside = await Task("Только начало", new DateOnly(2026, 10, 20), null);
        var spanning = await Task("Через всё окно", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1));
        var endsInside = await Task("Началась раньше", new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 2));
        await Task("До окна", new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30));
        await Task("После окна", null, new DateOnly(2026, 11, 1));
        await Task("Без дат", null, null);

        var result = (await db.SendAsync(new TaskCalendarQuery(Owner, from, to, board.Id)))!;

        Assert.Equal(new[] { dueInside, startInside, spanning, endsInside }.Order(), result.Items.Select(t => t.Id).Order());
        Assert.False(result.Truncated);
        await Assert.ThrowsAsync<ArgumentException>(() => db.SendAsync(new TaskCalendarQuery(Owner, to, from, board.Id)));
    }
}
