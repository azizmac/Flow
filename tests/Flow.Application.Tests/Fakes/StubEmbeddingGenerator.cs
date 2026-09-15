using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Заглушка эмбеддера для тестов фич: настоящая детерминированная реализация живёт в Flow.Infrastructure
/// (FakeEmbeddingGenerator), а здесь она была бы лишней зависимостью — Flow.Application.Tests про Infrastructure не знает.
/// </summary>
public sealed class StubEmbeddingGenerator : IEmbeddingGenerator
{
    public string ModelVersion => "stub:512:test";

    public int Dimensions => 512;

    /// <summary>Погашенный сайдкар: /search/status должен показывать embedderAvailable: false, а не падать.</summary>
    public bool Unavailable { get; set; }

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
        Unavailable
            ? throw new HttpRequestException("Эмбеддер недоступен (StubEmbeddingGenerator).")
            : Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => Unit()).ToArray());

    public Task<float[]> EmbedQueryAsync(string query, CancellationToken ct) =>
        Unavailable
            ? throw new HttpRequestException("Эмбеддер недоступен (StubEmbeddingGenerator).")
            : Task.FromResult(Unit());

    private float[] Unit()
    {
        var vector = new float[Dimensions];
        vector[0] = 1f;
        return vector;
    }
}
