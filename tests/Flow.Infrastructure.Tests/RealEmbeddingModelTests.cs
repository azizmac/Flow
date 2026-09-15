using Flow.Application.Abstractions;
using Flow.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Проверка настоящей модели: размерность, нормализация и то, что релевантный документ ближе случайного.
/// Из CI исключена ([Trait("Category", "Model")]) и без поднятого сайдкара тихо ничего не делает —
/// `dotnet test Flow.slnx` обязан быть зелёным на машине без модели.
///
/// Как запускать:
///   docker compose -f docker-compose.data.yml --profile ai up -d
///   FLOW_TEST_EMBEDDINGS_ENDPOINT=http://localhost:8090/v1 dotnet test tests/Flow.Infrastructure.Tests \
///     --filter "Category=Model"
/// </summary>
[Trait("Category", "Model")]
public class RealEmbeddingModelTests
{
    private const string EndpointVariable = "FLOW_TEST_EMBEDDINGS_ENDPOINT";

    private static IEmbeddingGenerator? CreateGenerator()
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointVariable);
        if (string.IsNullOrWhiteSpace(endpoint))
            return null;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Search:Enabled"] = "true",
                ["Search:Embeddings:QueryEndpoint"] = endpoint,
                ["Search:Embeddings:TimeoutSeconds"] = "30"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFlowSearch(configuration);
        return services.BuildServiceProvider().GetRequiredService<IEmbeddingGenerator>();
    }

    [Fact]
    public async Task Model_Should_Return_Normalized_512_Dimensional_Vectors()
    {
        var embeddings = CreateGenerator();
        if (embeddings is null)
            return;

        var vector = await embeddings.EmbedQueryAsync("почему падает экспорт в PDF", CancellationToken.None);

        Assert.Equal(512, vector.Length);
        Assert.Equal(1f, MathF.Sqrt(vector.Sum(v => v * v)), 2);
    }

    [Fact]
    public async Task Relevant_Document_Should_Be_Closer_Than_Random_One()
    {
        var embeddings = CreateGenerator();
        if (embeddings is null)
            return;

        var query = await embeddings.EmbedQueryAsync("падает экспорт отчёта в PDF", CancellationToken.None);
        var documents = await embeddings.EmbedDocumentsAsync(
            [
                "[FLW-1] Ошибка при выгрузке отчёта в PDF: кнопка «Скачать» отдаёт 500",
                "@ivan · Иван Петров · дизайнер интерфейсов"
            ],
            CancellationToken.None);

        Assert.True(Cosine(query, documents[0]) > Cosine(query, documents[1]));
    }

    private static float Cosine(float[] left, float[] right)
    {
        var dot = 0f;
        for (var i = 0; i < left.Length; i++)
            dot += left[i] * right[i];

        // Векторы уже нормализованы постобработкой, поэтому скалярное произведение и есть косинус.
        return dot;
    }
}
