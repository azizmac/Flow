namespace Flow.Application.Abstractions;

/// <summary>
/// Вектор поискового запроса с кэшем. Один и тот же запрос набирают повторно, а пагинация по выдаче
/// вообще идёт тем же текстом — платить за инференс каждый раз незачем.
/// </summary>
public interface IQueryEmbeddingCache
{
    /// <summary>
    /// Вектор нормализованной строки запроса. Бросает, если эмбеддер недоступен или не уложился
    /// в таймаут: решение о деградации принимает вызывающая сторона, а не кэш.
    /// </summary>
    Task<float[]> GetAsync(string normalizedQuery, CancellationToken cancellationToken);
}
