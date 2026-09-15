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
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Индексация на живом Postgres с pgvector: очередь, чанки, векторы, каскады и деградация
/// при недоступной модели. Эмбеддер — фейк, модель не поднимается.
/// </summary>
[Collection(SearchCollection.Name)]
public class SearchIndexPersistenceTests(SearchFixture fixture)
{
    private static readonly Guid Owner = SearchFixture.OwnerId;

    private static int _keySuffix;

    private async Task<BoardResponse> CreateBoardAsync()
    {
        var key = $"SRCH{Interlocked.Increment(ref _keySuffix)}";
        return (await fixture.SendAsync(new BoardCreateCommand(Owner, $"Проект {key}", key))).Response!;
    }

    private Task<TaskResponse?> CreateTaskAsync(Guid boardId, string title = "Падает экспорт отчёта", string? description = "Описание") =>
        fixture.SendAsync(new TaskCreateCommand(Owner, boardId, title, description, null));

    private Task<List<QueueRow>> QueueForAsync(SearchSourceType sourceType, Guid sourceId) =>
        fixture.QueryAsync(db => db.SearchIndexQueue
            .Where(r => r.SourceType == sourceType && r.SourceId == sourceId)
            .Select(r => new QueueRow(r.Operation, r.Priority, r.AttemptCount, r.LastError))
            .ToListAsync());

    private Task<List<ChunkRow>> ChunksForAsync(SearchSourceType sourceType, Guid sourceId) =>
        fixture.QueryAsync(db => db.SearchChunks
            .Where(c => c.SourceType == sourceType && c.SourceId == sourceId)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => new ChunkRow(c.ChunkIndex, c.Content, c.IsClosed, c.BoardId, c.ModelVersion))
            .ToListAsync());

    [Fact]
    public async Task Migration_Installs_Vector_Extension_And_Search_Indexes()
    {
        var extensions = await fixture.QueryAsync(db => db.Database
            .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_extension WHERE extname = 'vector'")
            .SingleAsync());

        var indexes = await fixture.QueryAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'SearchChunks'")
            .ToListAsync());

        Assert.Equal(1, extensions);
        Assert.Contains("IX_SearchChunks_Embedding_Hnsw", indexes);
        Assert.Contains("IX_SearchChunks_Tsv", indexes);
    }

    [Fact]
    public async Task CreateTask_Writes_Queue_Row()
    {
        var board = await CreateBoardAsync();

        var task = await CreateTaskAsync(board.Id);

        var row = Assert.Single(await QueueForAsync(SearchSourceType.Task, task!.Id));
        Assert.Equal(SearchIndexOperation.Upsert, row.Operation);
        Assert.Equal(0, row.Priority);
    }

    [Fact]
    public async Task Rolled_Back_Transaction_Leaves_Queue_Empty()
    {
        var board = await CreateBoardAsync();

        Guid taskId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var task = await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Откатится", null, null), CancellationToken.None);
            taskId = task!.Id;

            await transaction.RollbackAsync();
        }

        // Запись очереди коммитится той же транзакцией, что и задача: откат уносит обе.
        Assert.Empty(await QueueForAsync(SearchSourceType.Task, taskId));
        Assert.Null(await fixture.QueryAsync(db => db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId)));
    }

    [Fact]
    public async Task Indexing_Fills_Chunks_With_Vectors_And_Tsv()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id);

        await fixture.DrainIndexingAsync();

        var chunk = Assert.Single(await ChunksForAsync(SearchSourceType.Task, task!.Id));
        Assert.Equal(board.Id, chunk.BoardId);
        Assert.Contains("Падает экспорт отчёта", chunk.Content);
        Assert.Equal(fixture.Embedder.ModelVersion, chunk.ModelVersion);
        Assert.Empty(await QueueForAsync(SearchSourceType.Task, task.Id));

        // Вектор и полнотекстовый столбец заполнены: halfvec приехал, Tsv посчитала сама БД.
        var filled = await fixture.QueryAsync(db => db.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM \"SearchChunks\" " +
                "WHERE \"SourceId\" = {0} AND \"Embedding\" IS NOT NULL AND \"Tsv\" IS NOT NULL AND \"Tsv\" <> ''::tsvector",
                task.Id)
            .SingleAsync());

        Assert.Equal(1, filled);
    }

    [Fact]
    public async Task Second_Run_Without_Changes_Does_Not_Call_Embedder()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id);
        await fixture.DrainIndexingAsync();

        // Тот же текст — тот же ContentHash: модель звать незачем.
        var before = fixture.Embedder.Calls;
        await fixture.SendAsync(new TaskUpdateCommand(Owner, task!.Id, "Падает экспорт отчёта", "Описание", null));
        await fixture.DrainIndexingAsync();

        Assert.Equal(before, fixture.Embedder.Calls);
    }

    [Fact]
    public async Task Title_Change_Rebuilds_Chunks_And_DueDate_Does_Not()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id);
        await fixture.DrainIndexingAsync();

        await fixture.SendAsync(new TaskUpdateCommand(Owner, task!.Id, "Совсем другое название", null, null));
        await fixture.DrainIndexingAsync();

        var chunk = Assert.Single(await ChunksForAsync(SearchSourceType.Task, task.Id));
        Assert.Contains("Совсем другое название", chunk.Content);

        await fixture.SendAsync(new TaskSetDueDateCommand(Owner, task.Id, new DateOnly(2026, 3, 1)));

        // Срок текста не меняет — очередь не пополняется.
        Assert.Empty(await QueueForAsync(SearchSourceType.Task, task.Id));
    }

    [Fact]
    public async Task Status_Change_Updates_IsClosed_Without_Embedding()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id);
        await fixture.DrainIndexingAsync();

        var before = fixture.Embedder.Calls;
        var done = board.Statuses.First(s => s.IsFinal).Id;
        await fixture.SendAsync(new TaskUpdateCommand(Owner, task!.Id, null, null, done));
        await fixture.DrainIndexingAsync();

        var chunk = Assert.Single(await ChunksForAsync(SearchSourceType.Task, task.Id));
        Assert.True(chunk.IsClosed);
        Assert.Equal(before, fixture.Embedder.Calls);
    }

    [Fact]
    public async Task Comment_Is_Indexed_And_Removed_With_Its_Chunks()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id);
        var comment = (await fixture.SendAsync(new TaskCommentAddCommand(Owner, task!.Id, "Воспроизводится на проде"))).Response!;

        await fixture.DrainIndexingAsync();
        var chunk = Assert.Single(await ChunksForAsync(SearchSourceType.Comment, comment.Id));
        Assert.Equal("Воспроизводится на проде", chunk.Content);
        Assert.Equal(board.Id, chunk.BoardId);

        await fixture.SendAsync(new TaskCommentDeleteCommand(Owner, comment.Id));
        await fixture.DrainIndexingAsync();

        Assert.Empty(await ChunksForAsync(SearchSourceType.Comment, comment.Id));
    }

    [Fact]
    public async Task Deleting_Task_Removes_Chunks_Of_Task_And_Its_Comments()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id);
        var comment = (await fixture.SendAsync(new TaskCommentAddCommand(Owner, task!.Id, "Комментарий удаляемой задачи"))).Response!;
        await fixture.DrainIndexingAsync();

        await fixture.SendAsync(new TaskDeleteCommand(Owner, task.Id));
        await fixture.DrainIndexingAsync();

        Assert.Empty(await ChunksForAsync(SearchSourceType.Task, task.Id));
        Assert.Empty(await ChunksForAsync(SearchSourceType.Comment, comment.Id));
    }

    [Fact]
    public async Task Deleting_Board_Removes_Chunks_Of_Its_Tasks_And_Comments()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id);
        var comment = (await fixture.SendAsync(new TaskCommentAddCommand(Owner, task!.Id, "Комментарий удаляемого проекта"))).Response!;
        await fixture.DrainIndexingAsync();

        await fixture.SendAsync(new BoardDeleteCommand(Owner, board.Id));
        await fixture.DrainIndexingAsync();

        Assert.Empty(await ChunksForAsync(SearchSourceType.Board, board.Id));
        Assert.Empty(await ChunksForAsync(SearchSourceType.Task, task.Id));
        Assert.Empty(await ChunksForAsync(SearchSourceType.Comment, comment.Id));
    }

    [Fact]
    public async Task Long_Description_Is_Split_And_Shrinks_Back()
    {
        var board = await CreateBoardAsync();
        var paragraph = string.Join(' ', Enumerable.Repeat("Длинное предложение про экспорт отчёта.", 30));
        var task = await CreateTaskAsync(board.Id, "Длинная задача", $"{paragraph}\n\n{paragraph}");
        await fixture.DrainIndexingAsync();

        var chunks = await ChunksForAsync(SearchSourceType.Task, task!.Id);
        Assert.True(chunks.Count > 1, $"ожидалось несколько чанков, получено {chunks.Count}");

        await fixture.SendAsync(new TaskUpdateCommand(Owner, task.Id, null, "Коротко", null));
        await fixture.DrainIndexingAsync();

        // Источник стал короче — лишние чанки убраны, иначе поиск находил бы исчезнувший текст.
        var shrunk = Assert.Single(await ChunksForAsync(SearchSourceType.Task, task.Id));
        Assert.Contains("Коротко", shrunk.Content);
    }

    [Fact]
    public async Task Locked_Queue_Rows_Are_Skipped_By_Second_Worker()
    {
        var board = await CreateBoardAsync();
        // Очередь пуста: в проходе должна оказаться ровно одна запись — та, что будет заблокирована.
        await fixture.DrainIndexingAsync();
        var task = await CreateTaskAsync(board.Id);

        await using var holder = fixture.CreateScope();
        var db = holder.ServiceProvider.GetRequiredService<FlowDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        // Первый воркер держит строку: второй обязан её пропустить, а не ждать и не взять повторно.
        var locked = await db.SearchIndexQueue
            .FromSqlRaw("""
                        SELECT * FROM "SearchIndexQueue"
                        WHERE "SourceId" = {0}
                        FOR UPDATE SKIP LOCKED
                        """, task!.Id)
            .ToListAsync();

        Assert.Single(locked);

        var processed = await fixture.RunIndexingAsync();

        await transaction.RollbackAsync();

        Assert.Equal(0, processed);
        Assert.Empty(await ChunksForAsync(SearchSourceType.Task, task.Id));
    }

    [Fact]
    public async Task Unavailable_Embedder_Keeps_Request_In_Queue_With_Error()
    {
        var board = await CreateBoardAsync();
        await fixture.DrainIndexingAsync();

        fixture.Embedder.Unavailable = true;
        try
        {
            var task = await CreateTaskAsync(board.Id, "Задача без модели");
            await fixture.RunIndexingAsync();

            var row = Assert.Single(await QueueForAsync(SearchSourceType.Task, task!.Id));
            Assert.Equal(1, row.AttemptCount);
            Assert.NotNull(row.LastError);
            Assert.Empty(await ChunksForAsync(SearchSourceType.Task, task.Id));

            // API продолжает работать: следующая команда проходит как ни в чём не бывало.
            var another = await CreateTaskAsync(board.Id, "Ещё одна задача");
            Assert.NotNull(another);
        }
        finally
        {
            fixture.Embedder.Unavailable = false;
            // Застрявшая запись осталась бы в очереди с LastError и мешала остальным тестам.
            await fixture.ClearQueueAsync();
        }
    }

    [Fact]
    public async Task Reindex_Fills_All_Four_Types()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id, "Задача для переиндексации");
        await fixture.SendAsync(new TaskCommentAddCommand(Owner, task!.Id, "Комментарий для переиндексации"));

        // Индекс очищаем полностью: reindex должен собрать его заново из самих данных.
        await fixture.QueryAsync(db => db.SearchChunks.ExecuteDeleteAsync());
        await fixture.QueryAsync(db => db.SearchIndexQueue.ExecuteDeleteAsync());

        var enqueued = await fixture.SendAsync(new ReindexCommand(Owner, null, null));
        Assert.True(enqueued > 0);

        await fixture.DrainIndexingAsync();

        var status = await fixture.SendAsync(new SearchStatusQuery(Owner));
        Assert.True(status.Enabled);
        Assert.True(status.EmbedderAvailable);
        Assert.True(status.ChunksByType.Task > 0);
        Assert.True(status.ChunksByType.Comment > 0);
        Assert.True(status.ChunksByType.Board > 0);
        Assert.True(status.ChunksByType.User > 0);
    }

    [Fact]
    public async Task Live_Edit_Goes_Before_Bulk_Reindex()
    {
        var board = await CreateBoardAsync();
        await fixture.DrainIndexingAsync();

        await fixture.SendAsync(new ReindexCommand(Owner, null, null));
        var task = await CreateTaskAsync(board.Id, "Срочная правка");

        var first = await fixture.QueryAsync(db => db.SearchIndexQueue
            .FromSqlRaw("""
                        SELECT * FROM "SearchIndexQueue"
                        WHERE "NextAttemptAt" <= now()
                        ORDER BY "Priority", "EnqueuedAt"
                        LIMIT 1
                        """)
            .Select(r => new { r.SourceId, r.Priority })
            .SingleAsync());

        // Живая правка (Priority = 0) обгоняет массовую переиндексацию (Priority = 1).
        Assert.Equal(task!.Id, first.SourceId);
        Assert.Equal(0, first.Priority);

        await fixture.DrainIndexingAsync();
    }

    private sealed record QueueRow(SearchIndexOperation Operation, int Priority, int AttemptCount, string? LastError);

    private sealed record ChunkRow(int ChunkIndex, string Content, bool IsClosed, Guid? BoardId, string ModelVersion);
}
