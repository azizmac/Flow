using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Визуальная модель без модели: детерминированный вектор от хеша входа. Проверяется не качество
/// (его меряют вручную на реальных картинках), а то, зовут ли половину и переживает ли поиск её отказ.
/// </summary>
public sealed class FakeVisionEmbeddingGenerator(int dimensions = 512) : IVisionEmbeddingGenerator
{
    public string ModelVersion { get; } = "fake-vl:512:test";

    public bool IsConfigured { get; set; }

    /// <summary>Выставить, чтобы сымитировать погашенный сервис картинок.</summary>
    public bool Fails { get; set; }

    public int QueryCalls { get; private set; }

    public int ImageCalls { get; private set; }

    public string? LastQuery { get; private set; }

    public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        QueryCalls++;
        LastQuery = query;

        // Именно упавшая задача, а не синхронный бросок: настоящий клиент асинхронный, и вызывающий
        // ловит отказ на await — фейк, бросающий раньше, проверял бы не тот путь.
        if (Fails)
            return Task.FromException<float[]>(new TimeoutException("Визуальная модель не ответила (фейк)."));

        return Task.FromResult(Vector(query.GetHashCode()));
    }

    public Task<float[]> EmbedImageAsync(byte[] image, string contentType, CancellationToken cancellationToken)
    {
        ImageCalls++;

        if (Fails)
            return Task.FromException<float[]>(new TimeoutException("Визуальная модель не ответила (фейк)."));

        var seed = image.Length == 0 ? 0 : image[0] * 31 + image.Length;
        return Task.FromResult(Vector(seed));
    }

    /// <summary>Нормированный вектор, однозначно определяемый входом: одинаковые входы — одинаковые векторы.</summary>
    private float[] Vector(int seed)
    {
        var vector = new float[dimensions];
        var state = (uint)seed | 1u;

        for (var i = 0; i < dimensions; i++)
        {
            state = state * 1664525 + 1013904223;
            vector[i] = (state % 1000) / 1000f;
        }

        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        for (var i = 0; i < dimensions; i++)
            vector[i] /= norm;

        return vector;
    }
}
