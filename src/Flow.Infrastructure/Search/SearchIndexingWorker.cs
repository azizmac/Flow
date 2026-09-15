using System.Security.Cryptography;
using System.Text;
using Flow.Application.Abstractions;
using Flow.Infrastructure.Persistence;
using Flow.Infrastructure.Search.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Фоновая индексация: разбирает "SearchIndexQueue" и держит "SearchChunks" в согласии с источниками.
/// Живёт внутри Flow.Api как BackgroundService — отдельный Flow.Worker появится, когда индексация начнёт
/// мешать API по CPU, а не раньше.
///
/// Каждая запись берётся своей транзакцией через <c>FOR UPDATE SKIP LOCKED</c>: несколько экземпляров Api
/// разбирают одну очередь, не мешая друг другу и не обрабатывая запись дважды. Своя транзакция на запись,
/// а не на пачку, — чтобы падение одного источника не откатывало соседние.
/// </summary>
internal sealed class SearchIndexingWorker(
    IServiceScopeFactory scopes,
    SearchOptions options,
    ILogger<SearchIndexingWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(Math.Max(1, options.Indexing.PollIntervalSeconds));
        var didWork = false;

        logger.LogInformation("Индексация поиска запущена: батч {Batch}, опрос раз в {Poll}.",
            options.Indexing.BatchSize, poll);

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;

            try
            {
                processed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Сюда попадают только сбои вокруг очереди (БД недоступна): ошибки конкретных источников
                // обрабатываются внутри и уходят в AttemptCount/LastError.
                logger.LogError(ex, "Цикл индексации поиска упал; повтор через {Poll}.", poll);
                processed = 0;
            }

            if (processed > 0)
            {
                didWork = true;
                continue;
            }

            // Очередь разобрана — самое время убрать чанки прошлой версии модели. До этого момента они
            // нужны: пока идёт переиндексация, поиск работает на старых векторах, а не на пустоте.
            if (didWork)
            {
                didWork = false;
                await PurgeStaleVersionsAsync(stoppingToken);
            }

            try
            {
                await Task.Delay(poll, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Возвращает число разобранных записей; 0 — очередь пуста, можно поспать.</summary>
    internal async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var processed = 0;

        for (var i = 0; i < Math.Max(1, options.Indexing.BatchSize); i++)
        {
            if (!await ProcessNextAsync(cancellationToken))
                break;

            processed++;
        }

        return processed;
    }

    private async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var maxAttempts = options.Indexing.MaxAttempts;

        // AttemptCount < MaxAttempts прямо в выборке: исчерпавшая попытки запись остаётся лежать
        // и видна как queueStuck, но больше не крутит цикл вхолостую.
        var requests = await db.SearchIndexQueue
            .FromSql(
                $"""
                 SELECT * FROM "SearchIndexQueue"
                 WHERE "NextAttemptAt" <= now() AND "AttemptCount" < {maxAttempts}
                 ORDER BY "Priority", "EnqueuedAt"
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED
                 """)
            .ToListAsync(cancellationToken);

        var request = requests.FirstOrDefault();
        if (request is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        try
        {
            await ApplyAsync(scope.ServiceProvider, db, request, cancellationToken);

            db.SearchIndexQueue.Remove(request);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RecordFailureAsync(request, ex, cancellationToken);
        }

        return true;
    }

    private async Task ApplyAsync(
        IServiceProvider services,
        FlowDbContext db,
        SearchIndexRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Operation == SearchIndexOperation.Delete)
        {
            await DeleteAsync(db, request.SourceType, request.SourceId, cancellationToken);
            return;
        }

        var reader = services.GetRequiredService<SearchSourceReader>();
        var snapshot = await reader.ReadAsync(request.SourceType, request.SourceId, cancellationToken);

        // Источник исчез между постановкой в очередь и разбором — это тоже удаление, а не ошибка.
        if (snapshot is null || snapshot.Chunks.Count == 0)
        {
            await DeleteAsync(db, request.SourceType, request.SourceId, cancellationToken);
            return;
        }

        await UpsertAsync(db, services.GetRequiredService<IEmbeddingGenerator>(), request, snapshot, cancellationToken);
    }

    /// <summary>
    /// Удаление проекта уносит и чанки его задач и комментариев: они помечены тем же BoardId, отдельных
    /// записей очереди на каждый комментарий для этого не нужно.
    /// </summary>
    private static Task DeleteAsync(FlowDbContext db, SearchSourceType sourceType, Guid sourceId, CancellationToken cancellationToken) =>
        sourceType == SearchSourceType.Board
            ? db.SearchChunks.Where(c => c.BoardId == sourceId).ExecuteDeleteAsync(cancellationToken)
            : db.SearchChunks
                .Where(c => c.SourceType == sourceType && c.SourceId == sourceId)
                .ExecuteDeleteAsync(cancellationToken);

    private static async Task UpsertAsync(
        FlowDbContext db,
        IEmbeddingGenerator embeddings,
        SearchIndexRequest request,
        SourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var version = embeddings.ModelVersion;
        var now = DateTime.UtcNow;

        var existing = await db.SearchChunks
            .Where(c => c.SourceType == request.SourceType
                        && c.SourceId == request.SourceId
                        && c.ModelVersion == version)
            .ToListAsync(cancellationToken);

        var byIndex = existing.ToDictionary(c => c.ChunkIndex);
        var changed = new List<(SourceChunk Chunk, byte[] Hash)>();

        foreach (var chunk in snapshot.Chunks)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(chunk.Content));

            // Текст тот же и версия модели та же — вектор пересчитывать незачем. Именно так смена статуса
            // задачи обновляет IsClosed, ни разу не побеспокоив эмбеддер.
            if (byIndex.TryGetValue(chunk.Index, out var current) && current.ContentHash.SequenceEqual(hash))
            {
                Touch(current, snapshot, now);
                continue;
            }

            changed.Add((chunk, hash));
        }

        var vectors = await ResolveVectorsAsync(db, embeddings, request.SourceType, version, changed, cancellationToken);

        foreach (var (chunk, hash) in changed)
        {
            if (byIndex.TryGetValue(chunk.Index, out var current))
            {
                current.Content = chunk.Content;
                current.ContentHash = hash;
                current.Embedding = vectors[chunk.Index];
                Touch(current, snapshot, now);
                continue;
            }

            var added = new SearchChunk
            {
                Id = Guid.NewGuid(),
                SourceType = request.SourceType,
                SourceId = request.SourceId,
                ChunkIndex = chunk.Index,
                Content = chunk.Content,
                ContentHash = hash,
                Embedding = vectors[chunk.Index],
                ModelVersion = version
            };

            Touch(added, snapshot, now);
            db.SearchChunks.Add(added);
        }

        // Источник стал короче — хвост прошлой редакции убираем, иначе он останется в выдаче навсегда.
        foreach (var stale in existing.Where(c => c.ChunkIndex >= snapshot.Chunks.Count))
            db.SearchChunks.Remove(stale);
    }

    /// <summary>
    /// Сначала ищем готовый вектор по (тип источника, хеш, версия модели) — скопированный комментарий или
    /// продублированная задача не стоят ни одного вызова модели. Остаток уходит в эмбеддер одним батчем.
    /// </summary>
    private static async Task<Dictionary<int, HalfVector>> ResolveVectorsAsync(
        FlowDbContext db,
        IEmbeddingGenerator embeddings,
        SearchSourceType sourceType,
        string version,
        IReadOnlyList<(SourceChunk Chunk, byte[] Hash)> changed,
        CancellationToken cancellationToken)
    {
        var vectors = new Dictionary<int, HalfVector>();
        var missing = new List<SourceChunk>();

        foreach (var (chunk, hash) in changed)
        {
            var reused = await db.SearchChunks
                .Where(c => c.ModelVersion == version && c.SourceType == sourceType && c.ContentHash == hash)
                .Select(c => c.Embedding)
                .FirstOrDefaultAsync(cancellationToken);

            if (reused is not null)
                vectors[chunk.Index] = reused;
            else
                missing.Add(chunk);
        }

        if (missing.Count == 0)
            return vectors;

        var embedded = await embeddings.EmbedDocumentsAsync(
            missing.Select(chunk => chunk.EmbeddingInput).ToArray(),
            cancellationToken);

        for (var i = 0; i < missing.Count; i++)
            vectors[missing[i].Index] = ToHalfVector(embedded[i]);

        return vectors;
    }

    private static void Touch(SearchChunk chunk, SourceSnapshot snapshot, DateTime now)
    {
        chunk.BoardId = snapshot.BoardId;
        chunk.IsClosed = snapshot.IsClosed;
        chunk.SourceUpdatedAt = snapshot.SourceUpdatedAt;
        chunk.IndexedAt = now;
    }

    private static HalfVector ToHalfVector(float[] vector) =>
        new(Array.ConvertAll(vector, value => (Half)value));

    /// <summary>
    /// Отдельным соединением и без транзакции: транзакция записи уже откачена, и счётчик попыток должен
    /// пережить откат — иначе недоступный эмбеддер крутил бы одну и ту же запись вечно с AttemptCount = 0.
    /// </summary>
    private async Task RecordFailureAsync(SearchIndexRequest request, Exception error, CancellationToken cancellationToken)
    {
        var attempt = request.AttemptCount + 1;
        var delay = Backoff(attempt);
        var message = error.Message.Length <= 1000 ? error.Message : error.Message[..1000];

        logger.LogWarning(error,
            "Индексация {SourceType} {SourceId} не удалась (попытка {Attempt}); повтор через {Delay}.",
            request.SourceType, request.SourceId, attempt, delay);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE "SearchIndexQueue"
             SET "AttemptCount" = {attempt},
                 "NextAttemptAt" = {DateTime.UtcNow + delay},
                 "LastError" = {message}
             WHERE "Id" = {request.Id}
             """,
            cancellationToken);
    }

    private async Task PurgeStaleVersionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var embeddings = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
            var store = scope.ServiceProvider.GetRequiredService<SearchIndexStore>();

            var removed = await store.PurgeStaleVersionsAsync(embeddings.ModelVersion, cancellationToken);
            if (removed > 0)
                logger.LogInformation("Удалено {Count} чанков прошлых версий модели.", removed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Не удалось убрать чанки прошлых версий модели.");
        }
    }

    /// <summary>5 с → 5 мин, удвоением. Недоступный эмбеддер не должен долбить сайдкар каждые пять секунд.</summary>
    internal static TimeSpan Backoff(int attempt)
    {
        var seconds = MinBackoff.TotalSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }
}
