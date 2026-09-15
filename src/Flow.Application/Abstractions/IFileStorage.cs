namespace Flow.Application.Abstractions;

/// <summary>
/// Объектное хранилище файлов (S3-совместимое). Реализация — Flow.Infrastructure/Storage;
/// Application знает только про ключ и поток, ни про бакеты, ни про подписи запросов.
/// </summary>
public interface IFileStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Поток объекта; null — объекта нет (удалён руками, не доехал при сбое загрузки).</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Удаление несуществующего ключа — не ошибка: повторный вызов должен быть безопасен.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>Удаление пачкой по префиксу — для каскада при удалении задачи или проекта.</summary>
    Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken);
}
