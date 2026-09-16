using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Реранкер за HTTP: llama-server с <c>--reranking</c>, TEI или любой сервис с тем же контрактом
/// (<c>POST /rerank</c> — запрос, список документов, в ответ индексы с оценками). Контракт общий
/// у Jina, Cohere и llama.cpp, поэтому менять сервис можно без правки кода — только адрес в настройках.
///
/// Модель не пишет текст и ничего не знает про Flow: на вход идут пары «запрос — документ»,
/// на выход — числа. Всё, что связано с выдачей, остаётся в Application.
/// </summary>
internal sealed class HttpReranker(IHttpClientFactory factory, SearchOptions options, ILogger<HttpReranker> logger) : IReranker
{
    public const string HttpClientName = "flow-reranker";

    private readonly SearchRerankOptions _options = options.Rerank;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Endpoint);

    public async Task<IReadOnlyList<RerankedDocument>> RankAsync(
        string query,
        IReadOnlyList<string> documents,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured || documents.Count == 0)
            return [];

        var client = factory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(
                "rerank",
                new RerankRequest(_options.Model, query, documents),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Application гасит провал ступени и оставляет гибридный порядок, поэтому единственное
            // место, где видно причину, — здесь. Без лога «Точнее» молча не работало бы.
            logger.LogWarning(ex, "Реранкер {Endpoint} недоступен.", _options.Endpoint);
            throw;
        }

        using var _ = response;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Реранкер ответил {Status}: {Body}", (int)response.StatusCode, Shorten(body));
            throw new HttpRequestException(
                $"Реранкер ответил {(int)response.StatusCode}: {Shorten(body)}", null, response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken);
        if (payload?.Results is not { Count: > 0 } results)
        {
            logger.LogWarning("Реранкер вернул пустой ответ на {Count} документов.", documents.Count);
            return [];
        }

        // Чужие индексы игнорируем молча: это защита от сервиса, который вернул больше, чем прислали,
        // а не место для исключения — выдача пользователя от этого не должна падать.
        return results
            .Where(result => result.Index >= 0 && result.Index < documents.Count)
            .Select(result => new RerankedDocument(result.Index, result.Relevance))
            .ToArray();
    }

    private static string Shorten(string body) => body.Length <= 200 ? body : body[..200] + "…";

    private sealed record RerankRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("documents")] IReadOnlyList<string> Documents);

    private sealed record RerankResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<RerankResult>? Results);

    /// <param name="RelevanceScore">Имя поля у Jina, Cohere и llama.cpp.</param>
    /// <param name="RawScore">Часть сборок отдаёт ту же оценку под именем score.</param>
    private sealed record RerankResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] double RelevanceScore,
        [property: JsonPropertyName("score")] double? RawScore)
    {
        /// <summary>JsonIgnore обязателен: без него сериализатор видит два свойства с именем score.</summary>
        [JsonIgnore]
        public double Relevance => RawScore ?? RelevanceScore;
    }
}
