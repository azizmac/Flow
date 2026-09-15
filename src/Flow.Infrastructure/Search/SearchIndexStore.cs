using Flow.Application.Abstractions;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Чтение состояния индекса и массовая постановка в очередь. Переиндексация идёт целиком в БД
/// (<c>INSERT ... SELECT ... ON CONFLICT</c>), а не через выгрузку идентификаторов в память: доска на
/// сто тысяч задач не должна ни разу оказаться в куче процесса.
/// </summary>
internal sealed class SearchIndexStore(FlowDbContext db, SearchOptions options) : ISearchIndexStore
{
    /// <summary>Размер пачки массовой постановки — из ТЗ. Ограничивает длину одной транзакции, не память.</summary>
    private const int ReindexBatchSize = 1000;

    /// <summary>Приоритет массовой переиндексации: живые правки (0) всегда разбираются раньше.</summary>
    private const int ReindexPriority = 1;

    public async Task<SearchIndexStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        var maxAttempts = options.Indexing.MaxAttempts;

        var queueTotal = await db.SearchIndexQueue.CountAsync(cancellationToken);
        var queueStuck = await db.SearchIndexQueue.CountAsync(r => r.AttemptCount >= maxAttempts, cancellationToken);

        var oldestQueuedAt = await db.SearchIndexQueue
            .OrderBy(r => r.EnqueuedAt)
            .Select(r => (DateTime?)r.EnqueuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var chunksByType = await db.SearchChunks
            .GroupBy(c => c.SourceType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Type, x => x.Count, cancellationToken);

        return new SearchIndexStatistics(queueTotal, queueStuck, chunksByType, oldestQueuedAt);
    }

    public async Task<int> EnqueueAllAsync(
        IReadOnlyCollection<SearchSourceType> types,
        Guid? boardId,
        CancellationToken cancellationToken)
    {
        var queued = 0;

        foreach (var type in types.Distinct())
            queued += await EnqueueTypeAsync(type, boardId, cancellationToken);

        return queued;
    }

    /// <summary>
    /// Чанки прошлой версии модели живут, пока идёт переиндексация: иначе поиск на несколько часов
    /// остался бы вообще без данных. Чистятся одним DELETE, когда очередь разобрана.
    /// </summary>
    public Task<int> PurgeStaleVersionsAsync(string currentModelVersion, CancellationToken cancellationToken) =>
        db.SearchChunks
            .Where(c => c.ModelVersion != currentModelVersion)
            .ExecuteDeleteAsync(cancellationToken);

    private async Task<int> EnqueueTypeAsync(SearchSourceType type, Guid? boardId, CancellationToken cancellationToken)
    {
        var source = SourceSelect(type, boardId, out var boardFilterApplies);
        if (source is null)
            return 0;

        var total = 0;

        for (var offset = 0; ; offset += ReindexBatchSize)
        {
            // LIMIT живёт во вложенном запросе: если оставить его на верхнем SELECT, ON CONFLICT
            // окажется за LIMIT'ом и Postgres разберёт это иначе, чем задумано.
            var sql = $"""
                       INSERT INTO "SearchIndexQueue"
                           ("Id", "SourceType", "SourceId", "BoardId", "Operation", "Priority",
                            "EnqueuedAt", "AttemptCount", "NextAttemptAt", "LastError")
                       SELECT gen_random_uuid(), {(int)type}, page."SourceId", page."BoardId",
                              {(int)SearchIndexOperation.Upsert}, {ReindexPriority}, now(), 0, now(), NULL
                       FROM (
                           SELECT s."SourceId", s."BoardId"
                           FROM ({source}) AS s
                           ORDER BY s."SourceId"
                           LIMIT {ReindexBatchSize} OFFSET {offset}
                       ) AS page
                       ON CONFLICT ("SourceType", "SourceId", "Operation") DO UPDATE SET
                           "BoardId" = EXCLUDED."BoardId",
                           "EnqueuedAt" = EXCLUDED."EnqueuedAt",
                           "Priority" = LEAST("SearchIndexQueue"."Priority", EXCLUDED."Priority"),
                           "NextAttemptAt" = EXCLUDED."NextAttemptAt",
                           "AttemptCount" = 0,
                           "LastError" = NULL
                       """;

            // Каждая строка источника либо вставляется, либо обновляется — affected совпадает с размером выборки.
            var affected = boardFilterApplies && boardId is not null
                ? await db.Database.ExecuteSqlRawAsync(sql, new object[] { boardId.Value }, cancellationToken)
                : await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);

            total += affected;

            if (affected < ReindexBatchSize)
                return total;
        }
    }

    /// <summary>
    /// Подзапрос «что индексировать» для типа: пара (SourceId, BoardId). Параметр {0} — boardId,
    /// подставляется только если <paramref name="boardFilterApplies"/>.
    /// </summary>
    private static string? SourceSelect(SearchSourceType type, Guid? boardId, out bool boardFilterApplies)
    {
        boardFilterApplies = boardId is not null;

        switch (type)
        {
            case SearchSourceType.Task:
                return $"""
                        SELECT t."Id" AS "SourceId", t."BoardId" AS "BoardId"
                        FROM "TaskItems" t
                        {(boardId is null ? string.Empty : "WHERE t.\"BoardId\" = {0}")}
                        """;

            case SearchSourceType.Comment:
                return $"""
                        SELECT c."Id" AS "SourceId", t."BoardId" AS "BoardId"
                        FROM "TaskComments" c
                        JOIN "TaskItems" t ON t."Id" = c."TaskId"
                        {(boardId is null ? string.Empty : "WHERE t.\"BoardId\" = {0}")}
                        """;

            case SearchSourceType.Board:
                return $"""
                        SELECT b."Id" AS "SourceId", b."Id" AS "BoardId"
                        FROM "Boards" b
                        {(boardId is null ? string.Empty : "WHERE b.\"Id\" = {0}")}
                        """;

            case SearchSourceType.User:
                // Люди не принадлежат проекту, поэтому boardId для них не фильтр, а «не при чём».
                // Деактивированные пропускаются: их UserDeactivate убирает из индекса, и переиндексация
                // не должна возвращать ушедших обратно.
                boardFilterApplies = false;
                return $"""
                        SELECT u."Id" AS "SourceId", NULL::uuid AS "BoardId"
                        FROM "Users" u
                        WHERE u."Status" <> {(int)Flow.Domain.Entities.UserStatus.Deactivated}
                        """;

            default:
                // Attachment заведён заранее: источников у него пока нет, ставить в очередь нечего.
                boardFilterApplies = false;
                return null;
        }
    }
}
