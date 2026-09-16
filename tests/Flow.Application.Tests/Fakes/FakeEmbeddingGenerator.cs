using System.Security.Cryptography;
using System.Text;
using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Детерминированный эмбеддер: вектор — мешок слов, разложенный по измерениям хешом. Модель не нужна,
/// а близость всё-таки осмысленна — у текстов с общими словами она выше. Этого достаточно, чтобы
/// проверять ранжирование и свёртку выдачи, не поднимая llama-server (настоящее качество проверяют
/// тесты категории Model).
/// </summary>
public sealed class FakeEmbeddingGenerator(int dimensions = 512) : IEmbeddingGenerator
{
    private static readonly char[] Separators = [' ', '\n', '\r', '\t', '.', ',', ':', ';', '!', '?', '«', '»', '"', '(', ')', '[', ']', '-', '—'];

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
        ThrowIfUnavailable();

        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Vector).ToArray());
    }

    public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        ThrowIfUnavailable();

        return Task.FromResult(Vector(query));
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(!Unavailable);

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
            throw new InvalidOperationException("Эмбеддер недоступен (фейк).");
    }

    private float[] Vector(string text)
    {
        var vector = new float[Dimensions];

        foreach (var word in text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length < 3)
                continue;

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(word));
            vector[BitConverter.ToUInt16(hash, 0) % Dimensions] += 1f;
        }

        var norm = (float)Math.Sqrt(vector.Sum(value => (double)value * value));
        if (norm == 0)
        {
            // Текст без слов длиннее двух букв: детерминированный, но ни на что не похожий вектор.
            vector[0] = 1f;
            return vector;
        }

        for (var i = 0; i < Dimensions; i++)
            vector[i] /= norm;

        return vector;
    }
}
