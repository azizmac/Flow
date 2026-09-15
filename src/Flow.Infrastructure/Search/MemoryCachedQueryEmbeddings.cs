using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Кэш векторов поисковых запросов. Пагинация по одной выдаче идёт тем же текстом, да и запросы
/// у людей повторяются — инференс на каждую страницу был бы чистой тратой.
/// </summary>
internal sealed class MemoryCachedQueryEmbeddings(
    IMemoryCache cache,
    IEmbeddingGenerator embedder,
    SearchOptions options,
    ILogger<MemoryCachedQueryEmbeddings> logger)
    : IQueryEmbeddingCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public Task<float[]> GetAsync(string normalizedQuery, CancellationToken cancellationToken)
    {
        // Ключ включает версию модели: после смены модели старые векторы несравнимы с новыми.
        var key = $"search:q:{embedder.ModelVersion}:{normalizedQuery}";

        return cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            entry.Size = 1;

            var seconds = Math.Max(1, options.Embeddings.TimeoutSeconds);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));

            try
            {
                return await embedder.EmbedQueryAsync(normalizedQuery, timeout.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Не удалось получить вектор запроса — поиск уйдёт в полнотекстовый режим.");
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Эмбеддер не ответил за {Seconds} с — поиск уйдёт в полнотекстовый режим.", seconds);

                // Таймаут модели — повод деградировать в полнотекстовый режим, а не отменённый запрос
                // пользователя: отличаем одно от другого по тому, чей токен сработал.
                throw new TimeoutException($"Эмбеддер не ответил за {seconds} с.");
            }
        })!;
    }
}
