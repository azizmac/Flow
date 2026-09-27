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

    /// <summary>
    /// Копия объекта под новым ключом внутри бакета — без трафика через хост (слияние и перенос задачи,
    /// docs/TZ_task_model.md §6). Нет исходного объекта — false: строка вложения переедет, файла не будет, как и раньше.
    /// </summary>
    Task<bool> CopyAsync(string sourceKey, string targetKey, CancellationToken cancellationToken);

    /// <summary>Удаление несуществующего ключа — не ошибка: повторный вызов должен быть безопасен.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>Удаление пачкой по префиксу — для каскада при удалении задачи или проекта.</summary>
    Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken);
}
