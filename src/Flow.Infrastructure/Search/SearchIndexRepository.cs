using Flow.Application.Abstractions;
using Flow.Infrastructure.Persistence;
using Flow.Shared.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Статистика индекса для GET /search/status и массовая постановка в очередь для POST /search/reindex.
/// </summary>
internal sealed class SearchIndexRepository(FlowDbContext db) : ISearchIndexRepository
{
    /// <summary>Размер пачки массовой постановки: прогон идёт короткими запросами, а не одним на весь корпус.</summary>
    private const int ReindexBatchSize = 1000;

    /// <summary>Приоритет массовой переиндексации — позади живых правок (Priority = 0).</summary>
    private const int ReindexPriority = 1;

    private static readonly SearchSourceType[] AllTypes =
        [SearchSourceType.Task, SearchSourceType.Comment, SearchSourceType.Board, SearchSourceType.User, SearchSourceType.Attachment];

    public async Task<SearchIndexStatistics> GetStatisticsAsync(string modelVersion, int maxAttempts, CancellationToken cancellationToken)
    {
        var queueTotal = await db.SearchIndexQueue.CountAsync(cancellationToken);
        var queueStuck = await db.SearchIndexQueue.CountAsync(r => r.AttemptCount >= maxAttempts, cancellationToken);

        var oldestQueuedAt = await db.SearchIndexQueue
            .OrderBy(r => r.EnqueuedAt)
            .Select(r => (DateTime?)r.EnqueuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Только текущая версия модели: чанки прошлой версии живут рядом до конца переиндексации,
        // но поиск их не читает, и в статистике они дали бы ложную картину полноты индекса.
        var chunksByType = await db.SearchChunks
            .Where(c => c.ModelVersion == modelVersion)
            .GroupBy(c => c.SourceType)
            .Select(group => new { SourceType = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.SourceType, row => row.Count, cancellationToken);

        return new SearchIndexStatistics(queueTotal, queueStuck, chunksByType, oldestQueuedAt);
    }

    public async Task<int> EnqueueAllAsync(IReadOnlyCollection<SearchSourceType> types, Guid? boardId, CancellationToken cancellationToken)
    {
        var selected = types.Count == 0 ? AllTypes : types.Distinct().ToArray();

        var total = 0;
        foreach (var type in selected)
            total += await EnqueueTypeAsync(type, boardId, cancellationToken);

        return total;
    }

    /// <summary>
    /// Источники с чанками без вектора — след работы без модели. INSERT ... SELECT на стороне БД: строк здесь
    /// столько же, сколько источников успели проиндексировать «по словам», и все они уже в SearchChunks,
    /// откуда берутся и тип, и проект. Уже стоящие в очереди отсекает NOT EXISTS, как и при reindex,
    /// а упавшие и исчерпавшие MaxAttempts записи тех же источников сначала оживляются — см. <see cref="ResetFailedSql"/>.
    /// </summary>
    public async Task<int> EnqueueMissingVectorsAsync(string modelVersion, CancellationToken cancellationToken)
    {
        const string source =
            """
            SELECT DISTINCT c."SourceType", c."SourceId", c."BoardId"
            FROM "SearchChunks" c
            WHERE c."ModelVersion" = @model AND c."Embedding" IS NULL
            """;

        var now = DateTime.UtcNow;

        NpgsqlParameter[] Parameters() =>
        [
            new("model", modelVersion),
            new("operation", (int)SearchIndexOperation.Upsert),
            new("priority", ReindexPriority),
            new("now", NpgsqlDbType.TimestampTz) { Value = now }
        ];

        var reset = await db.Database.ExecuteSqlRawAsync(
            ResetFailedSql(source, "source.\"SourceType\""), Parameters(), cancellationToken);

        var inserted = await db.Database.ExecuteSqlRawAsync(
            $"""
             INSERT INTO "SearchIndexQueue"
                 ("Id", "SourceType", "SourceId", "BoardId", "Operation", "Priority", "EnqueuedAt", "AttemptCount", "NextAttemptAt", "LastError")
             SELECT gen_random_uuid(), source."SourceType", source."SourceId", source."BoardId", @operation, @priority, @now, 0, @now, NULL
             FROM ({source}) AS source
             WHERE NOT EXISTS (
                 SELECT 1 FROM "SearchIndexQueue" q
                 WHERE q."SourceType" = source."SourceType" AND q."SourceId" = source."SourceId" AND q."Operation" = @operation)
             ON CONFLICT ("SourceType", "SourceId", "Operation") DO NOTHING
             """,
            Parameters(),
            cancellationToken);

        return reset + inserted;
    }

    /// <summary>
    /// Ставит в очередь все источники типа пачками по <see cref="ReindexBatchSize"/>. Пачка целиком
    /// выполняется на стороне БД (INSERT ... SELECT): миллионы идентификаторов не едут в приложение.
    /// Уже стоящие в очереди источники отсекает NOT EXISTS — это и условие остановки цикла, и защита
    /// приоритета: живая правка (Priority = 0), поставленная до прогона, не должна уехать в конец.
    ///
    /// Записи, на которых индексация уже падала (в том числе исчерпавшие MaxAttempts), до цикла
    /// оживляются отдельным UPDATE — иначе NOT EXISTS отсёк бы их навсегда, и reindex после починки
    /// модели ничего бы не вернул в работу. Отдельным запросом, а не ON CONFLICT DO UPDATE в самой пачке:
    /// обновлённые строки ExecuteSqlRaw считает наравне со вставленными, и цикл с LIMIT, условие
    /// остановки которого «пачка ничего не вставила», крутился бы по одним и тем же записям вечно.
    /// </summary>
    private async Task<int> EnqueueTypeAsync(SearchSourceType type, Guid? boardId, CancellationToken cancellationToken)
    {
        var reset = await db.Database.ExecuteSqlRawAsync(
            ResetFailedSql(SourceQuery(type), "@sourceType"),
            [
                new NpgsqlParameter("sourceType", (int)type),
                new NpgsqlParameter("operation", (int)SearchIndexOperation.Upsert),
                new NpgsqlParameter("priority", ReindexPriority),
                new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = DateTime.UtcNow },
                new NpgsqlParameter("boardId", NpgsqlDbType.Uuid) { Value = (object?)boardId ?? DBNull.Value }
            ],
            cancellationToken);

        var total = reset;

        while (true)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("sourceType", (int)type),
                new NpgsqlParameter("operation", (int)SearchIndexOperation.Upsert),
                new NpgsqlParameter("priority", ReindexPriority),
                new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = DateTime.UtcNow },
                new NpgsqlParameter("boardId", NpgsqlDbType.Uuid) { Value = (object?)boardId ?? DBNull.Value },
                new NpgsqlParameter("limit", ReindexBatchSize)
            };

            var inserted = await db.Database.ExecuteSqlRawAsync(
                $"""
                 INSERT INTO "SearchIndexQueue"
                     ("Id", "SourceType", "SourceId", "BoardId", "Operation", "Priority", "EnqueuedAt", "AttemptCount", "NextAttemptAt", "LastError")
                 SELECT gen_random_uuid(), @sourceType, source."SourceId", source."BoardId", @operation, @priority, @now, 0, @now, NULL
                 FROM ({SourceQuery(type)}) AS source
                 WHERE NOT EXISTS (
                     SELECT 1 FROM "SearchIndexQueue" q
                     WHERE q."SourceType" = @sourceType AND q."SourceId" = source."SourceId" AND q."Operation" = @operation)
                 LIMIT @limit
                 ON CONFLICT ("SourceType", "SourceId", "Operation") DO NOTHING
                 """,
                parameters,
                cancellationToken);

            total += inserted;

            if (inserted == 0)
                return total;
        }
    }

    /// <summary>
    /// Оживляет упавшие записи очереди для источников из <paramref name="sourceQuery"/> — та же семантика,
    /// что у ON CONFLICT DO UPDATE в <see cref="SearchIndexQueue"/>: счётчик попыток в ноль, ошибка стёрта,
    /// взять можно сразу, приоритет не выше того, что уже был. Записи без ошибок не трогаются: их порядок
    /// в очереди (EnqueuedAt) и приоритет живой правки сохраняются. Ждущая на backoff запись тоже
    /// оживляется — массовый прогон обычно и запускают после того, как модель починили.
    /// </summary>
    /// <param name="sourceTypeExpression">Тип источника: параметр или колонка подзапроса.</param>
    private static string ResetFailedSql(string sourceQuery, string sourceTypeExpression) =>
        $"""
         UPDATE "SearchIndexQueue" q SET
             "BoardId" = COALESCE(source."BoardId", q."BoardId"),
             "Priority" = LEAST(q."Priority", @priority),
             "EnqueuedAt" = @now,
             "AttemptCount" = 0,
             "NextAttemptAt" = @now,
             "LastError" = NULL
         FROM ({sourceQuery}) AS source
         WHERE q."SourceType" = {sourceTypeExpression} AND q."SourceId" = source."SourceId" AND q."Operation" = @operation
             AND (q."AttemptCount" > 0 OR q."LastError" IS NOT NULL)
         """;

    /// <summary>
    /// Откуда брать источники типа. Строки константные — в SQL не подставляется ничего из запроса,
    /// фильтр по проекту идёт параметром.
    /// </summary>
    private static string SourceQuery(SearchSourceType type) => type switch
    {
        SearchSourceType.Task =>
            """
            SELECT t."Id" AS "SourceId", t."BoardId" AS "BoardId" FROM "TaskItems" t
            WHERE @boardId::uuid IS NULL OR t."BoardId" = @boardId
            """,

        SearchSourceType.Comment =>
            """
            SELECT c."Id" AS "SourceId", t."BoardId" AS "BoardId"
            FROM "TaskComments" c JOIN "TaskItems" t ON t."Id" = c."TaskId"
            WHERE @boardId::uuid IS NULL OR t."BoardId" = @boardId
            """,

        SearchSourceType.Board =>
            """
            SELECT b."Id" AS "SourceId", b."Id" AS "BoardId" FROM "Boards" b
            WHERE @boardId::uuid IS NULL OR b."Id" = @boardId
            """,

        SearchSourceType.Attachment =>
            """
            SELECT a."Id" AS "SourceId", a."BoardId" AS "BoardId" FROM "Attachments" a
            WHERE @boardId::uuid IS NULL OR a."BoardId" = @boardId
            """,

        // Люди к проекту не привязаны: при фильтре по проекту их не переиндексируют.
        SearchSourceType.User =>
            """
            SELECT u."Id" AS "SourceId", NULL::uuid AS "BoardId" FROM "Users" u
            WHERE @boardId::uuid IS NULL
            """,

        _ => throw new NotSupportedException($"Источник {type} пока не индексируется.")
    };
}
