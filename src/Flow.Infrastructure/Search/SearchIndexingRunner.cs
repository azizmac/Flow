using System.Security.Cryptography;
using System.Text;
using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Persistence;
using Flow.Infrastructure.Search.Entities;
using Flow.Shared.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Один проход очереди индексации. Вынесен из <see cref="SearchIndexingWorker"/> отдельным классом,
/// чтобы проход можно было выполнить в тесте явно, не поднимая фоновый цикл и не подгоняя таймеры.
/// </summary>
internal sealed class SearchIndexingRunner(
    FlowDbContext db,
    SearchSourceReader sources,
    IEmbeddingGenerator embedder,
    IVisionEmbeddingGenerator vision,
    SearchOptions options,
    ILogger<SearchIndexingRunner> logger)
{
    /// <summary>5 с → 5 мин: первая попытка почти сразу, дальше экспоненциально до потолка.</summary>
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>Сколько записей очереди разобрано. 0 — очередь пуста, воркеру можно поспать.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var batch = await ClaimAsync(cancellationToken);
        if (batch.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        foreach (var request in batch)
        {
            // Savepoint: ошибка на одном источнике (нет доступа к модели, битые данные) не должна
            // откатывать уже обработанные записи батча.
            await transaction.CreateSavepointAsync("item", cancellationToken);
            try
            {
                await ProcessAsync(request, cancellationToken);
                await DeleteRequestAsync(request.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await transaction.RollbackToSavepointAsync("item", cancellationToken);
                await FailAsync(request, ex, cancellationToken);
                logger.LogWarning(ex, "Не удалось проиндексировать {SourceType} {SourceId}.", request.SourceType, request.SourceId);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    /// <summary>
    /// Берёт пачку записей очереди. FOR UPDATE SKIP LOCKED обязателен: несколько экземпляров Flow.Api
    /// разбирают одну очередь и не должны ни ждать друг друга, ни взять одну запись дважды.
    /// </summary>
    private async Task<IReadOnlyList<SearchIndexRequest>> ClaimAsync(CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, options.Indexing.BatchSize);
        var maxAttempts = options.Indexing.MaxAttempts;

        return await db.SearchIndexQueue
            .FromSql($"""
                      SELECT * FROM "SearchIndexQueue"
                      WHERE "NextAttemptAt" <= now() AND "AttemptCount" < {maxAttempts}
                      ORDER BY "Priority", "EnqueuedAt"
                      LIMIT {batchSize}
                      FOR UPDATE SKIP LOCKED
                      """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task ProcessAsync(SearchIndexRequest request, CancellationToken cancellationToken)
    {
        if (request.Operation == SearchIndexOperation.Delete)
        {
            await DeleteChunksAsync(request.SourceType, request.SourceId, cancellationToken);
            return;
        }

        var snapshot = await sources.ReadAsync(request.SourceType, request.SourceId, cancellationToken);
        if (snapshot is null || snapshot.Chunks.Count == 0)
        {
            // Источник исчез или остался без текста — в индексе ему делать нечего.
            await DeleteChunksAsync(request.SourceType, request.SourceId, cancellationToken);
            return;
        }

        await UpsertChunksAsync(request.SourceType, request.SourceId, snapshot, cancellationToken);
        await UpsertVisualChunksAsync(request.SourceType, request.SourceId, snapshot, cancellationToken);
    }

    /// <summary>
    /// Визуальные чанки вложения — отдельные строки со своей ModelVersion: вектор кадра лежит
    /// в другом пространстве и сравнивать его с текстовыми нельзя. Текстовый upsert их не трогает
    /// (он фильтрует по своей версии), поэтому у картинки в индексе два представления сразу:
    /// имя файла текстом и содержимое кадра вектором.
    ///
    /// Кадров бывает несколько: у скана это первые страницы, каждая своим чанком со своим номером.
    /// Содержимое чанка — подпись кадра (имя файла, у скана со страницей): она уходит в заголовок
    /// выдачи, а вектор считается не по ней, а по самому кадру.
    /// </summary>
    private async Task UpsertVisualChunksAsync(
        SearchSourceType sourceType,
        Guid sourceId,
        SourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var modelVersion = vision.ModelVersion;

        var existing = await db.SearchChunks
            .Where(c => c.SourceType == sourceType && c.SourceId == sourceId && c.ModelVersion == modelVersion)
            .ToListAsync(cancellationToken);

        var images = vision.IsConfigured ? snapshot.Images ?? [] : [];

        if (images.Count == 0)
        {
            // Кадр удалили, заменили на документ с текстом или визуальную половину выключили — старые
            // векторы в индексе оставлять нельзя, они продолжат находиться.
            db.SearchChunks.RemoveRange(existing);
            if (existing.Count > 0)
                await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var changed = false;

        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            var hash = SHA256.HashData(image.Content);
            var chunk = existing.FirstOrDefault(c => c.ChunkIndex == index);

            // Тот же кадр — вектор уже посчитан: прогон визуальной модели дороже текстовой на порядок.
            if (chunk is not null && chunk.ContentHash.AsSpan().SequenceEqual(hash))
            {
                chunk.IsClosed = snapshot.IsClosed;
                chunk.BoardId = snapshot.BoardId;
                chunk.SourceUpdatedAt = snapshot.SourceUpdatedAt;
                changed = true;
                continue;
            }

            var embedding = await vision.EmbedImageAsync(image.Content, image.ContentType, cancellationToken);

            chunk ??= db.SearchChunks.Add(new SearchChunk
            {
                SourceType = sourceType,
                SourceId = sourceId,
                ChunkIndex = index,
                ModelVersion = modelVersion
            }).Entity;

            chunk.BoardId = snapshot.BoardId;
            chunk.Content = image.Label;
            chunk.ContentHash = hash;
            chunk.IsClosed = snapshot.IsClosed;
            chunk.SourceUpdatedAt = snapshot.SourceUpdatedAt;
            chunk.IndexedAt = DateTime.UtcNow;
            chunk.Embedding = ToHalfVector(embedding);
            changed = true;
        }

        // Страниц стало меньше (файл заменили) — лишние чанки убираем, иначе поиск найдёт исчезнувшую.
        foreach (var extra in existing.Where(c => c.ChunkIndex >= images.Count))
        {
            db.SearchChunks.Remove(extra);
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Удаление проекта уносит и чанки его задач и комментариев: сами они уже ушли каскадом БД,
    /// и отдельной записи очереди на каждый не нужно — чанк помнит свой проект в BoardId.
    /// </summary>
    private async Task DeleteChunksAsync(SearchSourceType sourceType, Guid sourceId, CancellationToken cancellationToken)
    {
        // Без фильтра по версии модели: у источника могут быть и текстовые чанки, и визуальный,
        // и при удалении должны уйти оба.
        await db.SearchChunks
            .Where(c => c.SourceType == sourceType && c.SourceId == sourceId)
            .ExecuteDeleteAsync(cancellationToken);

        if (sourceType == SearchSourceType.Board)
            await db.SearchChunks.Where(c => c.BoardId == sourceId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task UpsertChunksAsync(SearchSourceType sourceType, Guid sourceId, SourceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var modelVersion = embedder.ModelVersion;
        var now = DateTime.UtcNow;

        var existing = await db.SearchChunks
            .Where(c => c.SourceType == sourceType && c.SourceId == sourceId && c.ModelVersion == modelVersion)
            .ToListAsync(cancellationToken);

        var desired = snapshot.Chunks
            .Select(chunk => new DesiredChunk(chunk, Sha256(chunk.Content)))
            .ToArray();

        // Чанки, чей текст не изменился, переписывать незачем: у них меняются только флаги источника
        // (IsClosed после смены статуса) — модель в этом случае не зовут вовсе. Исключение — чанк без
        // вектора: его записали при выключенных эмбеддингах, и теперь текст тот же, а прогнать надо.
        var stale = desired
            .Where(item => Find(existing, item.Chunk.Index) is not { } match
                           || match.Embedding is null
                           || !match.ContentHash.AsSpan().SequenceEqual(item.Hash))
            .ToArray();

        // Модель выключена — чанки пишутся без вектора: текст остаётся находимым полнотекстом,
        // а векторы дозаполнятся, когда модель вернётся (SearchIndexingWorker ставит их в очередь).
        var vectors = options.Embeddings.Enabled
            ? await EmbedAsync(stale, modelVersion, cancellationToken)
            : [];

        foreach (var item in desired)
        {
            var chunk = Find(existing, item.Chunk.Index);
            if (chunk is null)
            {
                chunk = new SearchChunk
                {
                    SourceType = sourceType,
                    SourceId = sourceId,
                    ChunkIndex = item.Chunk.Index,
                    ModelVersion = modelVersion
                };
                db.SearchChunks.Add(chunk);
            }

            chunk.BoardId = snapshot.BoardId;
            chunk.Content = item.Chunk.Content;
            chunk.ContentHash = item.Hash;
            chunk.IsClosed = snapshot.IsClosed;
            chunk.SourceUpdatedAt = snapshot.SourceUpdatedAt;
            chunk.IndexedAt = now;

            if (vectors.TryGetValue(item.Chunk.Index, out var embedding))
                chunk.Embedding = embedding;
        }

        // Источник стал короче — лишние чанки убираем, иначе поиск продолжит находить исчезнувший текст.
        foreach (var extra in existing.Where(c => c.ChunkIndex >= desired.Length))
            db.SearchChunks.Remove(extra);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Векторы для изменившихся чанков. Сначала ищется готовый вектор того же текста и той же версии
    /// модели — у другого источника он вполне может быть (продублированные задачи, шаблонные
    /// комментарии); в эмбеддер уходит только остаток, одним батчем.
    /// </summary>
    private async Task<Dictionary<int, HalfVector>> EmbedAsync(
        IReadOnlyList<DesiredChunk> stale,
        string modelVersion,
        CancellationToken cancellationToken)
    {
        var vectors = new Dictionary<int, HalfVector>();
        if (stale.Count == 0)
            return vectors;

        var hashes = stale.Select(item => item.Hash).ToArray();

        var reusable = await db.SearchChunks
            .Where(c => c.ModelVersion == modelVersion && c.Embedding != null && hashes.Contains(c.ContentHash))
            .Select(c => new { c.ContentHash, c.Embedding })
            .ToListAsync(cancellationToken);

        var byHash = new Dictionary<string, HalfVector>();
        foreach (var item in reusable)
            byHash.TryAdd(Key(item.ContentHash), item.Embedding!);

        var toEmbed = stale.Where(item => !byHash.ContainsKey(Key(item.Hash))).ToArray();
        if (toEmbed.Length > 0)
        {
            var embeddings = await embedder.EmbedDocumentsAsync(
                toEmbed.Select(item => item.Chunk.EmbeddingInput).ToArray(),
                cancellationToken);

            if (embeddings.Count != toEmbed.Length)
                throw new InvalidOperationException($"Эмбеддер вернул {embeddings.Count} векторов на {toEmbed.Length} чанков.");

            for (var i = 0; i < toEmbed.Length; i++)
                byHash[Key(toEmbed[i].Hash)] = ToHalfVector(embeddings[i]);
        }

        foreach (var item in stale)
            vectors[item.Chunk.Index] = byHash[Key(item.Hash)];

        return vectors;
    }

    private Task DeleteRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        db.SearchIndexQueue.Where(r => r.Id == requestId).ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Неудача не теряет запись: растёт AttemptCount, откладывается NextAttemptAt, пишется LastError.
    /// После MaxAttempts запись остаётся в очереди и попадает в queueStuck — данные не пропадают,
    /// а погашенная модель не роняет API.
    /// </summary>
    private async Task FailAsync(SearchIndexRequest request, Exception error, CancellationToken cancellationToken)
    {
        var attempt = request.AttemptCount + 1;
        var seconds = Math.Min(MaxBackoff.TotalSeconds, MinBackoff.TotalSeconds * Math.Pow(2, attempt - 1));
        // Вместе с внутренними: у DbUpdateException снаружи только «не удалось сохранить», а причина внутри.
        var full = string.Join(" → ", Unwrap(error));
        var message = full.Length <= 1000 ? full : full[..1000];

        var parameters = new[]
        {
            new NpgsqlParameter("id", request.Id),
            new NpgsqlParameter("attempt", attempt),
            new NpgsqlParameter("next", NpgsqlDbType.TimestampTz) { Value = DateTime.UtcNow.AddSeconds(seconds) },
            new NpgsqlParameter("error", message)
        };

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "SearchIndexQueue"
            SET "AttemptCount" = @attempt, "NextAttemptAt" = @next, "LastError" = @error
            WHERE "Id" = @id
            """,
            parameters,
            cancellationToken);
    }

    private static IEnumerable<string> Unwrap(Exception? error)
    {
        for (var current = error; current is not null; current = current.InnerException)
            yield return current.Message;
    }

    private static SearchChunk? Find(List<SearchChunk> chunks, int index) =>
        chunks.FirstOrDefault(c => c.ChunkIndex == index);

    private static string Key(byte[] hash) => Convert.ToHexStringLower(hash);

    private static byte[] Sha256(string content) => SHA256.HashData(Encoding.UTF8.GetBytes(content));

    private static HalfVector ToHalfVector(float[] embedding) =>
        new(embedding.Select(value => (Half)value).ToArray());

    /// <param name="Hash">SHA-256 от Content: ключ «текст не менялся» и ключ переиспользования вектора.</param>
    private sealed record DesiredChunk(SourceChunk Chunk, byte[] Hash);
}
