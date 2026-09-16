namespace Flow.Application.Abstractions;

/// <summary>
/// Визуальные эмбеддинги (docs/TZ_search_vector.md, «Мультимодальность»). Отдельная модель от текстовой:
/// она кладёт картинку и текст запроса в одно пространство, поэтому скриншот находится по описанию
/// того, что на нём происходит, — без OCR.
///
/// Векторы этой модели несравнимы с текстовыми, у них своя <see cref="ModelVersion"/>, и каждая половина
/// поиска ищет только среди своих чанков. Слияние — тем же RRF: ему нужны ранги, а не сопоставимые оценки.
/// </summary>
public interface IVisionEmbeddingGenerator
{
    /// <summary>Версия модели: в неё входят имя и размерность. Пишется в чанк и отделяет два пространства.</summary>
    string ModelVersion { get; }

    /// <summary>Настроен ли адрес модели. false — визуальная половина не выполняется вовсе.</summary>
    bool IsConfigured { get; }

    /// <summary>Вектор текста запроса — в том же пространстве, что и картинки.</summary>
    Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken);

    /// <summary>Вектор картинки. contentType нужен модели как есть — она сама решает, как её масштабировать.</summary>
    Task<float[]> EmbedImageAsync(byte[] image, string contentType, CancellationToken cancellationToken);
}
