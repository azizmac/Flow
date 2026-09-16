using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Вторая ступень без модели: запоминает, что ей прислали, и переворачивает порядок — этого хватает,
/// чтобы отличить «выдача переупорядочена» от «осталась гибридной». Настоящее качество меряет golden-set.
/// </summary>
public sealed class FakeReranker : IReranker
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>Выставить, чтобы сымитировать погашенный или не ответивший вовремя сервис.</summary>
    public bool Fails { get; set; }

    /// <summary>Вернуть пустой ответ — сервис жив, но оценок не дал.</summary>
    public bool ReturnsNothing { get; set; }

    public int Calls { get; private set; }

    public IReadOnlyList<string> LastDocuments { get; private set; } = [];

    public string? LastQuery { get; private set; }

    public Task<IReadOnlyList<RerankedDocument>> RankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken)
    {
        Calls++;
        LastQuery = query;
        LastDocuments = documents;

        if (Fails)
            throw new TimeoutException("Реранкер не ответил (фейк).");

        if (ReturnsNothing)
            return Task.FromResult<IReadOnlyList<RerankedDocument>>([]);

        // Обратный порядок: оценка тем выше, чем дальше документ стоял в гибридной выдаче.
        var scores = documents
            .Select((_, index) => new RerankedDocument(index, index))
            .ToArray();

        return Task.FromResult<IReadOnlyList<RerankedDocument>>(scores);
    }
}
