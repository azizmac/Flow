namespace Flow.Infrastructure.Search;

/// <summary>
/// Константы схемы индекса. Размерность вектора зашита в тип столбца <c>halfvec(512)</c> и в имена
/// индексов, поэтому она здесь, а не только в конфигурации: сменить её можно лишь миграцией,
/// а не правкой appsettings. Несовпадение с <c>Search:Embeddings:Dimensions</c> проверяется на старте.
/// </summary>
internal static class SearchSchema
{
    public const int Dimensions = 512;

    public const string ChunksTable = "SearchChunks";

    public const string QueueTable = "SearchIndexQueue";

    /// <summary>Конфигурация словаря для tsvector. Русский словарь идёт в стандартной поставке Postgres.</summary>
    public const string TextSearchConfiguration = "russian";

    public const string TsvColumn = "Tsv";
}
