using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Эмбеддер за OpenAI-совместимым HTTP: сайдкар llama-server с Qwen3-Embedding (POST {endpoint}/embeddings).
/// Клиентов два — запросы и индексация: у них разные таймауты и они могут смотреть на разные машины
/// (GPU для запросов, CPU для прогона индекса), см. «Разделение CPU и GPU» в базовом ТЗ.
/// </summary>
internal sealed class HttpEmbeddingGenerator : IEmbeddingGenerator
{
    public const string QueryClientName = "embeddings-query";
    public const string IndexingClientName = "embeddings-indexing";

    private readonly IHttpClientFactory _clients;
    private readonly SearchEmbeddingsOptions _options;
    private readonly ILogger<HttpEmbeddingGenerator> _logger;

    public HttpEmbeddingGenerator(IHttpClientFactory clients, SearchOptions options, ILogger<HttpEmbeddingGenerator> logger)
    {
        _clients = clients;
        _options = options.Embeddings;
        _logger = logger;
        ModelVersion = Qwen3Embeddings.BuildModelVersion(_options.Model, _options.Dimensions, _options.QueryInstruction);
    }

    public string ModelVersion { get; }

    public int Dimensions => _options.Dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
            return [];

        var result = new List<float[]>(texts.Count);

        // Батчи ограничены BatchSize: у сайдкара свой лимит на размер запроса, а один большой батч
        // при ошибке пришлось бы повторять целиком.
        foreach (var batch in texts.Chunk(Math.Max(1, _options.BatchSize)))
            result.AddRange(await EmbedAsync(IndexingClientName, batch, cancellationToken));

        return result;
    }

    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        var wrapped = Qwen3Embeddings.WrapQuery(_options.QueryInstruction, query);
        var vectors = await EmbedAsync(QueryClientName, [wrapped], cancellationToken);
        return vectors[0];
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EmbedAsync(QueryClientName, ["ping"], cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            // Недоступность модели — деградация поиска, а не сбой: /search/status покажет false.
            _logger.LogDebug(ex, "Эмбеддер недоступен.");
            return false;
        }
    }

    private async Task<IReadOnlyList<float[]>> EmbedAsync(string clientName, IReadOnlyList<string> input, CancellationToken cancellationToken)
    {
        var client = _clients.CreateClient(clientName);

        var response = await client.PostAsJsonAsync("embeddings", new EmbeddingsRequest(_options.Model, input), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Эмбеддер ответил {(int)response.StatusCode}: {Trim(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Эмбеддер вернул пустой ответ.");

        if (payload.Data is null || payload.Data.Count != input.Count)
            throw new InvalidOperationException(
                $"Эмбеддер вернул {payload.Data?.Count ?? 0} векторов на {input.Count} текстов.");

        return payload.Data
            .OrderBy(item => item.Index)
            .Select(item => Qwen3Embeddings.Reduce(item.Embedding, _options.Dimensions))
            .ToArray();
    }

    private static string Trim(string body) => body.Length <= 500 ? body : body[..500] + "…";

    private sealed record EmbeddingsRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingsItem>? Data);

    private sealed record EmbeddingsItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
