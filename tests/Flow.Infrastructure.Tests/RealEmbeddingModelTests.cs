using Flow.Application.Features.Search;
using Flow.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Единственные тесты, которым нужна настоящая модель. Из CI исключены фильтром Category!=Model —
/// запускаются вручную при смене модели, размерности или инструкции:
/// <code>
/// docker compose -f docker-compose.data.yml --profile ai up -d
/// dotnet test tests/Flow.Infrastructure.Tests --filter "Category=Model"
/// </code>
/// Адрес сайдкара — переменная окружения FLOW_TEST_EMBEDDINGS_ENDPOINT (по умолчанию localhost:8081).
/// </summary>
[Trait("Category", "Model")]
public class RealEmbeddingModelTests
{
    private static HttpEmbeddingGenerator Create()
    {
        var endpoint = Environment.GetEnvironmentVariable("FLOW_TEST_EMBEDDINGS_ENDPOINT") ?? "http://localhost:8081/v1";

        var options = new SearchOptions
        {
            Enabled = true,
            Embeddings = new SearchEmbeddingsOptions
            {
                Model = Environment.GetEnvironmentVariable("FLOW_TEST_EMBEDDINGS_MODEL") ?? "Qwen3-Embedding-0.6B",
                QueryEndpoint = endpoint,
                Dimensions = 512,
                TimeoutSeconds = 60
            }
        };

        return new HttpEmbeddingGenerator(
            new SingleEndpointHttpClientFactory(endpoint),
            options,
            NullLogger<HttpEmbeddingGenerator>.Instance);
    }

    /// <summary>
    /// Без поднятой модели тест не падает, а ничего не проверяет: `dotnet test Flow.slnx` обязан быть
    /// зелёным на машине без llama-server (xUnit 2 не умеет помечать тест пропущенным на лету).
    /// </summary>
    private static async Task<HttpEmbeddingGenerator?> CreateIfRunningAsync()
    {
        var embedder = Create();
        return await embedder.IsAvailableAsync(CancellationToken.None) ? embedder : null;
    }

    [Fact]
    public async Task Model_Returns_Normalized_Vector_Of_Configured_Size()
    {
        if (await CreateIfRunningAsync() is not { } embedder)
            return;

        var vector = await embedder.EmbedQueryAsync("падает экспорт отчёта", CancellationToken.None);

        Assert.Equal(512, vector.Length);
        Assert.Equal(1.0, Qwen3Embeddings.Norm(vector), Qwen3Embeddings.NormTolerance);
    }

    [Fact]
    public async Task Relevant_Document_Is_Closer_Than_Random_One()
    {
        if (await CreateIfRunningAsync() is not { } embedder)
            return;

        var query = await embedder.EmbedQueryAsync("не выгружается отчёт в PDF", CancellationToken.None);
        var documents = await embedder.EmbedDocumentsAsync(
            [
                "[FLW-1] Падает экспорт отчёта в PDF\nПри нажатии «Скачать» вместо файла приходит ошибка 500.",
                "[FLW-2] Обновить логотип на странице входа\nДизайн прислал новый знак, нужно заменить картинку."
            ],
            CancellationToken.None);

        var relevant = Cosine(query, documents[0]);
        var random = Cosine(query, documents[1]);

        Assert.True(relevant > random, $"релевантный {relevant:F3} должен быть ближе случайного {random:F3}");
    }

    /// <summary>Векторы нормализованы, поэтому косинус — это просто скалярное произведение.</summary>
    private static double Cosine(float[] left, float[] right) =>
        left.Zip(right, (a, b) => (double)a * b).Sum();

    private sealed class SingleEndpointHttpClientFactory(string endpoint) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new()
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }
}
