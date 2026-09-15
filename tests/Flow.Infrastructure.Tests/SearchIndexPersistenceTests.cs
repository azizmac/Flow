using Flow.Application.Abstractions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Search.Commands.ReindexCommand;
using Flow.Application.Features.Search.Queries.SearchStatusQuery;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Infrastructure.Persistence;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Индексация на настоящем Postgres с pgvector: очередь, чанки, векторы, удаления и устойчивость к
/// погашенному эмбеддеру (ТЗ поиска, этапы 1–3). Модель не поднимается — работает FakeEmbeddingGenerator.
/// Коллекция общая, поэтому каждый тест сам доводит очередь до пустоты, прежде чем считать вызовы эмбеддера.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SearchIndexPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    private static string Key() => "S" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<BoardResponse> CreateBoardAsync(string name = "Поиск") =>
        (await db.SendAsync(new BoardCreateCommand(Owner, name, Key()))).Response!;

    private Task<int> ChunkCountAsync(SearchSourceType type, Guid sourceId) =>
        db.QueryAsync(ctx => ctx.SearchChunks.CountAsync(c => c.SourceType == type && c.SourceId == sourceId));

    private Task<int> QueueCountAsync(Guid sourceId) =>
        db.QueryAsync(ctx => ctx.SearchIndexQueue.CountAsync(r => r.SourceId == sourceId));

    private Task ExecuteAsync(string sql) =>
        db.QueryAsync(ctx => ctx.Database.ExecuteSqlRawAsync(sql));

    [Fact]
    public async Task CreateTask_Should_Enqueue_And_Index_With_Vector_And_Tsvector()
    {
        var board = await CreateBoardAsync();
        var task = (await db.SendAsync(new TaskCreateCommand(
            Owner, board.Id, "Падает экспорт отчёта", "Кнопка «Скачать PDF» отдаёт 500.", null)))!;

        Assert.Equal(1, await QueueCountAsync(task.Id));

        await db.RunIndexingAsync();

        Assert.Equal(0, await QueueCountAsync(task.Id));

        var chunk = await db.QueryAsync(ctx => ctx.SearchChunks
            .SingleAsync(c => c.SourceType == SearchSourceType.Task && c.SourceId == task.Id));

        Assert.Equal(board.Id, chunk.BoardId);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Contains("Падает экспорт отчёта", chunk.Content);
        // Шапка уходит только в эмбеддер: в Content кода задачи быть не должно.
        Assert.DoesNotContain($"[{task.Code}]", chunk.Content);
        Assert.Equal(512, chunk.Embedding.Memory.Length);
        Assert.False(chunk.IsClosed);
        Assert.Equal(db.Embeddings.ModelVersion, chunk.ModelVersion);

        // Tsv — GENERATED STORED: считает его БД, и он не может разойтись с Content.
        var filled = await db.QueryAsync(ctx => ctx.Database
            .SqlQuery<int>($"""
                            SELECT count(*)::int AS "Value" FROM "SearchChunks"
                            WHERE "SourceId" = {task.Id} AND "Tsv" IS NOT NULL AND "Tsv" <> ''::tsvector
                            """)
            .SingleAsync());

        Assert.Equal(1, filled);
    }

    [Fact]
    public async Task Rollback_Should_Leave_Queue_Empty()
    {
        var board = await CreateBoardAsync();

        // Постановка в очередь коммитится той же транзакцией, что и сама задача: откат уносит обе.
        await using var scope = db.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlowDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await using var transaction = await context.Database.BeginTransactionAsync();
        var task = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Откатится", null, null), CancellationToken.None))!;
        Assert.Equal(1, await context.SearchIndexQueue.CountAsync(r => r.SourceId == task.Id));

        await transaction.RollbackAsync();

        Assert.Equal(0, await QueueCountAsync(task.Id));
    }

    [Fact]
    public async Task Reindexing_Unchanged_Source_Should_Not_Call_Embedder()
    {
        var board = await CreateBoardAsync();
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Не менялась", null, null)))!;
        await db.RunIndexingAsync();

        await db.SendAsync(new ReindexCommand(Owner, [SearchSourceType.Task], board.Id));
        db.Embeddings.ResetCalls();

        await db.RunIndexingAsync();

        // Текст тот же и версия модели та же — вектор пересчитывать незачем.
        Assert.Equal(0, db.Embeddings.Calls);
        Assert.Equal(1, await ChunkCountAsync(SearchSourceType.Task, task.Id));
    }

    [Fact]
    public async Task StatusChange_Should_Update_IsClosed_Without_Embedding()
    {
        var board = await CreateBoardAsync();
        var finalStatusId = board.Statuses.Single(s => s.IsFinal).Id;
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Закроется", null, null)))!;
        await db.RunIndexingAsync();
        db.Embeddings.ResetCalls();

        await db.SendAsync(new TaskUpdateCommand(Owner, task.Id, null, null, finalStatusId));
        await db.RunIndexingAsync();

        var chunk = await db.QueryAsync(ctx => ctx.SearchChunks
            .SingleAsync(c => c.SourceType == SearchSourceType.Task && c.SourceId == task.Id));

        Assert.True(chunk.IsClosed);
        Assert.Equal(0, db.Embeddings.Calls);
    }

    [Fact]
    public async Task TitleChange_Should_Rebuild_Chunk_And_DueDate_Should_Not()
    {
        var board = await CreateBoardAsync();
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Старое название", null, null)))!;
        await db.RunIndexingAsync();

        await db.SendAsync(new TaskUpdateCommand(Owner, task.Id, "Новое название", null, null));
        await db.RunIndexingAsync();

        var chunk = await db.QueryAsync(ctx => ctx.SearchChunks
            .SingleAsync(c => c.SourceType == SearchSourceType.Task && c.SourceId == task.Id));
        Assert.Contains("Новое название", chunk.Content);

        await db.SendAsync(new TaskSetDueDateCommand(Owner, task.Id, new DateOnly(2026, 12, 31)));

        // Срок текста не меняет — очередь не пополняется.
        Assert.Equal(0, await QueueCountAsync(task.Id));
    }

    [Fact]
    public async Task Deleting_Sources_Should_Remove_Their_Chunks()
    {
        var board = await CreateBoardAsync();
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Удалится", null, null)))!;
        var comment = (await db.SendAsync(new TaskCommentAddCommand(Owner, task.Id, "и комментарий тоже"))).Response!;
        await db.RunIndexingAsync();

        Assert.Equal(1, await ChunkCountAsync(SearchSourceType.Comment, comment.Id));

        await db.SendAsync(new TaskCommentDeleteCommand(Owner, comment.Id));
        await db.RunIndexingAsync();
        Assert.Equal(0, await ChunkCountAsync(SearchSourceType.Comment, comment.Id));

        await db.SendAsync(new TaskDeleteCommand(Owner, task.Id));
        await db.RunIndexingAsync();
        Assert.Equal(0, await ChunkCountAsync(SearchSourceType.Task, task.Id));
    }

    [Fact]
    public async Task Deleting_Board_Should_Remove_Chunks_Of_Its_Tasks_And_Comments()
    {
        var board = await CreateBoardAsync("Уйдёт целиком");
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Задача проекта", null, null)))!;
        await db.SendAsync(new TaskCommentAddCommand(Owner, task.Id, "комментарий задачи проекта"));
        await db.RunIndexingAsync();

        Assert.True(await db.QueryAsync(ctx => ctx.SearchChunks.AnyAsync(c => c.BoardId == board.Id)));

        await db.SendAsync(new BoardDeleteCommand(Owner, board.Id));
        await db.RunIndexingAsync();

        // Чанки задач и комментариев помечены BoardId проекта — уходят вместе с ним.
        Assert.False(await db.QueryAsync(ctx => ctx.SearchChunks.AnyAsync(c => c.BoardId == board.Id)));
    }

    [Fact]
    public async Task Unavailable_Embedder_Should_Grow_AttemptCount_And_Keep_Api_Working()
    {
        var board = await CreateBoardAsync();
        await db.RunIndexingAsync();

        Guid taskId;
        db.Embeddings.ThrowOnEmbed = true;
        try
        {
            // Сам API продолжает работать: недоступность эмбеддера не должна давать 5xx.
            taskId = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Эмбеддер лежит", null, null)))!.Id;

            await db.RunIndexingAsync(passes: 1);

            var request = await db.QueryAsync(ctx => ctx.SearchIndexQueue.SingleAsync(r => r.SourceId == taskId));
            Assert.Equal(1, request.AttemptCount);
            Assert.False(string.IsNullOrWhiteSpace(request.LastError));
            Assert.True(request.NextAttemptAt > DateTime.UtcNow);
            Assert.Equal(0, await ChunkCountAsync(SearchSourceType.Task, taskId));
        }
        finally
        {
            db.Embeddings.ThrowOnEmbed = false;
            // Запись с отложенным повтором пережила бы тест и всплыла бы в соседних, где считают вызовы эмбеддера.
            await ExecuteAsync($"DELETE FROM \"SearchIndexQueue\" WHERE \"SourceId\" = '{taskId}'");
        }
    }

    [Fact]
    public async Task Two_Workers_Should_Not_Process_Same_Request_Twice()
    {
        var board = await CreateBoardAsync();
        await db.RunIndexingAsync();

        var taskIds = new List<Guid>();
        for (var i = 0; i < 6; i++)
            taskIds.Add((await db.SendAsync(new TaskCreateCommand(Owner, board.Id, $"Параллельная {i}", null, null)))!.Id);

        var first = db.CreateWorker();
        var second = db.CreateWorker();

        // SKIP LOCKED: воркеры делят очередь, а не спорят за одни и те же записи.
        await Task.WhenAll(
            first.ProcessBatchAsync(CancellationToken.None),
            second.ProcessBatchAsync(CancellationToken.None));

        await db.RunIndexingAsync();

        Assert.Equal(0, await db.QueryAsync(ctx => ctx.SearchIndexQueue.CountAsync(r => taskIds.Contains(r.SourceId))));

        foreach (var taskId in taskIds)
            Assert.Equal(1, await ChunkCountAsync(SearchSourceType.Task, taskId));
    }

    [Fact]
    public async Task Reindex_Should_Fill_All_Four_Types_And_Live_Edit_Goes_First()
    {
        var board = await CreateBoardAsync("Переиндексация");
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Переиндексируется", null, null)))!;
        await db.SendAsync(new TaskCommentAddCommand(Owner, task.Id, "комментарий для переиндексации"));
        await db.RunIndexingAsync();

        await ExecuteAsync("DELETE FROM \"SearchChunks\"");

        var queued = await db.SendAsync(new ReindexCommand(Owner));
        Assert.True(queued >= 4);

        // Живая правка после массовой постановки обязана обработаться раньше: у неё Priority = 0.
        var urgent = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Срочная правка", null, null)))!;

        var firstOfTwo = await db.QueryAsync(ctx => ctx.SearchIndexQueue
            .Where(r => r.SourceId == urgent.Id || r.SourceId == task.Id)
            .OrderBy(r => r.Priority).ThenBy(r => r.EnqueuedAt)
            .Select(r => r.SourceId)
            .FirstAsync());
        Assert.Equal(urgent.Id, firstOfTwo);

        await db.RunIndexingAsync(passes: 60);

        var status = await db.SendAsync(new SearchStatusQuery(Owner));
        Assert.True(status.Enabled);
        Assert.True(status.ChunksByType.Task > 0);
        Assert.True(status.ChunksByType.Comment > 0);
        Assert.True(status.ChunksByType.Board > 0);
        Assert.True(status.ChunksByType.User > 0);
    }
}
