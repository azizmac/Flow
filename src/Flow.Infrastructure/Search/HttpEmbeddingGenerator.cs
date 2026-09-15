using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flow.Application.Abstractions;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Эмбеддер за OpenAI-совместимым HTTP (<c>POST {endpoint}/embeddings</c>) — сайдкар llama-server.
/// Два клиента вместо одного: запрос пользователя должен отваливаться за секунды (иначе поиск «висит»),
/// а индексация спокойно ждёт минуту на большом батче. Один таймаут на оба сценария всегда неправильный.
/// </summary>
internal sealed class HttpEmbeddingGenerator : IEmbeddingGenerator
{
    public const string QueryClientName = "embeddings-query";
    public const string IndexingClientName = "embeddings-indexing";

    /// <summary>Индексация не торопится: батч из BatchSize текстов на CPU считается заметно дольше секунды.</summary>
    public const int IndexingTimeoutSeconds = 60;

    private readonly IHttpClientFactory _clients;
    private readonly SearchEmbeddingOptions _options;

    public HttpEmbeddingGenerator(IHttpClientFactory clients, SearchOptions options)
    {
        _clients = clients;
        _options = options.Embeddings;
        ModelVersion = Qwen3Embeddings.BuildModelVersion(_options.Model, _options.Dimensions, _options.QueryInstruction);
    }

    public string ModelVersion { get; }

    public int Dimensions => _options.Dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
            return [];

        var batchSize = Math.Max(1, _options.BatchSize);
        var result = new List<float[]>(texts.Count);

        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize).ToArray();
            result.AddRange(await EmbedAsync(IndexingClientName, IndexingEndpoint, batch, ct));
        }

        return result;
    }

    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken ct)
    {
        var wrapped = Qwen3Embeddings.WrapQuery(_options.QueryInstruction, query);
        var vectors = await EmbedAsync(QueryClientName, _options.QueryEndpoint, [wrapped], ct);
        return vectors[0];
    }

    /// <summary>Пустой IndexingEndpoint означает «тот же, что QueryEndpoint»: одна модель на оба сценария.</summary>
    private string IndexingEndpoint =>
        string.IsNullOrWhiteSpace(_options.IndexingEndpoint) ? _options.QueryEndpoint : _options.IndexingEndpoint;

    private async Task<IReadOnlyList<float[]>> EmbedAsync(
        string clientName,
        string endpoint,
        IReadOnlyList<string> input,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(
                "Search:Embeddings:QueryEndpoint не настроен — адрес сайдкара эмбеддингов неизвестен.");

        var client = _clients.CreateClient(clientName);
        var url = $"{endpoint.TrimEnd('/')}/embeddings";

        using var response = await client.PostAsJsonAsync(url, new EmbeddingsRequest(_options.Model, input), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Эмбеддер ответил {(int)response.StatusCode} на {url}: {Shorten(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingsResponse>(ct)
            ?? throw new InvalidOperationException($"Эмбеддер вернул пустой ответ на {url}.");

        if (payload.Data is null || payload.Data.Count != input.Count)
            throw new InvalidOperationException(
                $"Эмбеддер вернул {payload.Data?.Count ?? 0} векторов на {input.Count} текстов.");

        // llama-server сохраняет порядок, но контракт OpenAI этого не обещает — раскладываем по index.
        return payload.Data
            .OrderBy(item => item.Index)
            .Select(item => Qwen3Embeddings.Shrink(item.Embedding ?? [], _options.Dimensions))
            .ToArray();
    }

    private static string Shorten(string body) => body.Length <= 500 ? body : body[..500] + "…";

    private sealed record EmbeddingsRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingsItem>? Data);

    private sealed record EmbeddingsItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
