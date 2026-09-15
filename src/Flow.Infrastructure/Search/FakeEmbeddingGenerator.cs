using System.Security.Cryptography;
using System.Text;
using Flow.Application.Abstractions;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Детерминированный эмбеддер без модели: вектор разворачивается из SHA-256 текста. Живёт в основном
/// проекте, а не в тестовом, потому что нужен всем трём тестовым сборкам сразу — иначе пришлось бы
/// тянуть двухгигабайтную модель в CI ради проверки того, что очередь пустеет.
/// Похожесть текстов он не моделирует и моделировать не должен: качество выдачи проверяется
/// отдельными тестами с настоящей моделью ([Trait("Category", "Model")]).
/// </summary>
public sealed class FakeEmbeddingGenerator(int dimensions = 512) : IEmbeddingGenerator
{
    private int _calls;

    public string ModelVersion { get; set; } = "fake:512:test";

    public int Dimensions { get; } = dimensions;

    /// <summary>Сколько раз звали эмбеддер. Тесты на переиспользование векторов смотрят именно сюда.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Симуляция погашенного сайдкара: все вызовы падают, как настоящий HttpRequestException.</summary>
    public bool ThrowOnEmbed { get; set; }

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        Count(texts.Count);
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToArray());
    }

    public Task<float[]> EmbedQueryAsync(string query, CancellationToken ct)
    {
        Count(1);
        return Task.FromResult(Embed(Qwen3Embeddings.WrapQuery("test", query)));
    }

    public void ResetCalls() => Interlocked.Exchange(ref _calls, 0);

    private void Count(int texts)
    {
        if (ThrowOnEmbed)
            throw new HttpRequestException("Эмбеддер недоступен (FakeEmbeddingGenerator.ThrowOnEmbed).");

        Interlocked.Add(ref _calls, texts);
    }

    private float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        for (var i = 0; i < Dimensions; i++)
            vector[i] = (seed[i % seed.Length] + i) % 251 - 125f;

        return Qwen3Embeddings.Normalize(vector);
    }
}
