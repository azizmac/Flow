namespace Flow.Application.Abstractions;

/// <summary>
/// Считает векторы для индексации и для запроса. Реализуется в Flow.Infrastructure
/// (HttpEmbeddingGenerator поверх сайдкара llama-server; FakeEmbeddingGenerator для тестов).
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>Версия модели: {модель}:{размерность}:{хеш инструкции}. Векторы разных версий несовместимы.</summary>
    string ModelVersion { get; }

    int Dimensions { get; }

    /// <summary>Документы — без инструкции, как есть.</summary>
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct);

    /// <summary>Запрос — с Instruct-префиксом. Асимметрия обязательна, иначе качество падает молча.</summary>
    Task<float[]> EmbedQueryAsync(string query, CancellationToken ct);
}
