using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Shared.Contracts.Boards;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Сводный список задач (GET /tasks). Тесты идут по общей базе коллекции, поэтому запрос «по всем проектам»
/// видит и задачи соседних тестов: проверяем вхождение своих задач, а не точный состав ответа.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskSearchPersistenceTests(PostgresFixture db)
{
    [Fact]
    public async Task Search_Should_SpanBoards_And_FilterByBoard()
    {
        var first = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Span one", "SPN1"))).Response!;
        var second = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Span two", "SPN2"))).Response!;
        var here = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, first.Id, "Здесь", null, null)))!;
        var there = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, second.Id, "Там", null, null)))!;

        var all = await db.SendAsync(new TaskSearchQuery(Limit: 500));

        Assert.Contains(all.Items, t => t.Id == here.Id);
        Assert.Contains(all.Items, t => t.Id == there.Id);

        var onlyFirst = await db.SendAsync(new TaskSearchQuery(BoardId: first.Id));

        Assert.Equal(here.Id, Assert.Single(onlyFirst.Items).Id);
        Assert.Equal(1, onlyFirst.Total);
    }

    [Fact]
    public async Task Search_Should_FilterByStatusType_Across_Boards()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Types", "TYP"))).Response!;
        var inProgress = board.Statuses.First(s => s.Type == StatusType.InProgress);
        var done = board.Statuses.First(s => s.Type == StatusType.Done);
        var working = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "В работе", null, inProgress.Id)))!;
        await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Готово", null, done.Id));

        var result = await db.SendAsync(new TaskSearchQuery(BoardId: board.Id, StatusType: StatusType.InProgress));

        Assert.Equal(working.Id, Assert.Single(result.Items).Id);

        // Счётчики не сужаются выбранным статусом: кнопки фильтра показывают, сколько найдётся при переключении.
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.ByType.Single(c => c.Type == StatusType.InProgress).Count);
        Assert.Equal(1, result.ByType.Single(c => c.Type == StatusType.Done).Count);
    }

    [Fact]
    public async Task Search_Should_MatchTitleAndCode_CaseInsensitively()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Query", "QRY"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Починить Аватарку", null, null)))!;

        var byTitle = await db.SendAsync(new TaskSearchQuery(Query: "аватарк"));
        var byCode = await db.SendAsync(new TaskSearchQuery(Query: "qry-"));

        Assert.Contains(byTitle.Items, t => t.Id == task.Id);
        Assert.Contains(byCode.Items, t => t.Id == task.Id);
    }

    [Fact]
    public async Task Search_Should_PageWithCursor_Newest_First()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Paging", "PGN"))).Response!;
        var created = new List<Guid>();
        for (var i = 1; i <= 5; i++)
            created.Add((await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, $"Задача {i}", null, null)))!.Id);

        var page = await db.SendAsync(new TaskSearchQuery(BoardId: board.Id, Limit: 2));
        Assert.Equal(2, page.Items.Count);
        Assert.NotNull(page.NextCursor);
        Assert.Equal(5, page.Total);

        var seen = page.Items.Select(t => t.Id).ToList();
        var cursor = page.NextCursor;
        while (cursor is not null)
        {
            var next = await db.SendAsync(new TaskSearchQuery(BoardId: board.Id, Limit: 2, Cursor: cursor));
            seen.AddRange(next.Items.Select(t => t.Id));
            cursor = next.NextCursor;
        }

        // Ни одна задача не потеряна и не повторилась, порядок — от новых к старым.
        Assert.Equal(created.AsEnumerable().Reverse(), seen);
    }
}
