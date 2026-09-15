using System.Security.Cryptography;
using System.Text;
using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Детерминированный эмбеддер: вектор выводится из хеша текста. Один и тот же текст всегда даёт один
/// и тот же вектор, разные — разные; модель при этом не нужна, и тесты не зависят от сети.
/// </summary>
public sealed class FakeEmbeddingGenerator(int dimensions = 512) : IEmbeddingGenerator
{
    private int _calls;

    /// <summary>Сколько раз звали модель — так проверяется «неизменившийся текст не переэмбеддивается».</summary>
    public int Calls => _calls;

    /// <summary>Выставить в true, чтобы сымитировать погашенный сайдкар.</summary>
    public bool Unavailable { get; set; }

    public string ModelVersion { get; } = "fake:512:test";

    public int Dimensions { get; } = dimensions;

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        if (Unavailable)
            throw new InvalidOperationException("Эмбеддер недоступен (фейк).");

        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Vector).ToArray());
    }

    public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        if (Unavailable)
            throw new InvalidOperationException("Эмбеддер недоступен (фейк).");

        return Task.FromResult(Vector(query));
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(!Unavailable);

    private float[] Vector(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var vector = new float[Dimensions];

        for (var i = 0; i < Dimensions; i++)
            vector[i] = (hash[i % hash.Length] - 128) / 128f;

        // Нормализуем, как настоящий эмбеддер: косинусное расстояние в тестах считается так же.
        var norm = (float)Math.Sqrt(vector.Sum(value => (double)value * value));
        for (var i = 0; i < Dimensions; i++)
            vector[i] /= norm;

        return vector;
    }
}
