namespace Flow.Application.Abstractions;

/// <summary>
/// Превращает текст в вектор. Реализация живёт в Flow.Infrastructure (HTTP-сайдкар llama-server,
/// позже — Onnx в процессе); Application знает только про этот интерфейс.
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>Версия модели: {модель}:{размерность}:{хеш инструкции}. Векторы разных версий несовместимы.</summary>
    string ModelVersion { get; }

    int Dimensions { get; }

    /// <summary>Документы — без инструкции, как есть.</summary>
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);

    /// <summary>Запрос — с Instruct-префиксом. Асимметрия обязательна, иначе качество падает молча.</summary>
    Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken);

    /// <summary>Пробный вызов для GET /search/status: true — модель отвечает.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
