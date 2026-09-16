using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Визуальная модель за HTTP: vLLM с <c>--runner pooling</c> (см. docker-compose.data.yml, сервис
/// embeddings-vl). Текст уходит обычным полем <c>input</c>, картинка — полем <c>messages</c> с
/// <c>image_url</c>: у OpenAI-совместимого эндпоинта эмбеддингов другого способа передать изображение нет.
///
/// llama.cpp сюда не годится, и это проверено: проектор он грузит и картинку в чате видит, но из
/// эндпоинта эмбеддингов изображение выбрасывает — вектор красного квадрата совпадает с вектором
/// синего до четвёртого знака. Поэтому для визуальной ветки именно vLLM, как и предполагает ТЗ.
/// </summary>
internal sealed class HttpVisionEmbeddingGenerator(
    IHttpClientFactory factory,
    SearchOptions options,
    ILogger<HttpVisionEmbeddingGenerator> logger) : IVisionEmbeddingGenerator
{
    public const string HttpClientName = "flow-embeddings-vl";

    private readonly SearchVisionOptions _options = options.Embeddings.Vision;

    /// <summary>Инструкции у визуальной половины нет — в версию входят только имя модели и размерность.</summary>
    public string ModelVersion { get; } =
        Qwen3Embeddings.BuildModelVersion(options.Embeddings.Vision.Model, options.Embeddings.Vision.Dimensions, string.Empty);

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Endpoint);

    public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken) =>
        SendAsync(new { model = _options.Model, input = query }, "запрос", cancellationToken);

    public Task<float[]> EmbedImageAsync(byte[] image, string contentType, CancellationToken cancellationToken)
    {
        var url = $"data:{contentType};base64,{Convert.ToBase64String(image)}";
        var request = new
        {
            model = _options.Model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[] { new { type = "image_url", image_url = new { url } } }
                }
            }
        };

        return SendAsync(request, "картинку", cancellationToken);
    }

    private async Task<float[]> SendAsync(object request, string what, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Визуальная модель не настроена (Search:Embeddings:Vision).");

        var client = factory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("embeddings", request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Визуальная модель {Endpoint} недоступна.", _options.Endpoint);
            throw;
        }

        using var _ = response;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Визуальная модель ответила {Status} на {What}: {Body}", (int)response.StatusCode, what, Shorten(body));
            throw new HttpRequestException($"Визуальная модель ответила {(int)response.StatusCode}: {Shorten(body)}", null, response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingsResponse>(cancellationToken);
        var raw = payload?.Data is [{ Embedding: { Count: > 0 } vector }, ..]
            ? vector
            : throw new InvalidOperationException($"Визуальная модель вернула пустой ответ на {what}.");

        // Та же постобработка, что у текстовой половины: MRL-урезание и перенормализация.
        // Без неё косинус ломается — норма обрезанного вектора меньше единицы.
        return Qwen3Embeddings.Reduce(raw, _options.Dimensions);
    }

    private static string Shorten(string body) => body.Length <= 200 ? body : body[..200] + "…";

    private sealed record EmbeddingsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingItem>? Data);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("embedding")] IReadOnlyList<float>? Embedding);
}
