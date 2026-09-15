using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Выдача без БД: запоминает критерии, с которыми пришёл хендлер, и отдаёт заданный ответ.
/// Так проверяется решение хендлера — какие половины гибрида выполнять и с какими лимитами,
/// — а не SQL (его проверяют интеграционные тесты).
/// </summary>
public sealed class FakeSearchQueryRepository : ISearchQueryRepository
{
    public SearchCriteria? LastCriteria { get; private set; }

    public SearchPage Page { get; set; } = new([], 0);

    public Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        LastCriteria = criteria;
        return Task.FromResult(Page);
    }
}

/// <summary>Кэш векторов запроса: либо отдаёт вектор фейкового эмбеддера, либо падает, изображая погашенную модель.</summary>
public sealed class FakeQueryEmbeddingCache(FakeEmbeddingGenerator embedder) : IQueryEmbeddingCache
{
    public bool Fails { get; set; }

    public int Calls { get; private set; }

    public Task<float[]> GetAsync(string normalizedQuery, CancellationToken cancellationToken)
    {
        Calls++;

        if (Fails)
            throw new TimeoutException("Эмбеддер не ответил (фейк).");

        return embedder.EmbedQueryAsync(normalizedQuery, cancellationToken);
    }
}
