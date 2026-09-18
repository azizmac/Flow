using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Визуальная модель за HTTP: тот же llama-server в router-режиме, что отдаёт текстовые эмбеддинги
/// и реранк (модуль ai). Отдельного сервиса у визуальной половины больше нет — есть третья секция
/// в docker/ai/models.ini с ключом mmproj.
///
/// Записанный в проекте вердикт «llama.cpp выбрасывает картинку из эндпоинта эмбеддингов: вектор
/// красного квадрата совпадает с вектором синего» был верен по наблюдению и неверен по выводу.
/// Проверял он не llama.cpp, а форму запроса: тело уходило в виде vLLM (messages с image_url),
/// а llama-server такое тело на эмбеддингах не разбирает вовсе и эмбеддит текстовый остаток.
/// В собственном формате — prompt_string с маркером медиа плюс multimodal_data — картинка через
/// проектор проходит. Замер на GTX 1650 SUPER, Qwen3-VL-Embedding-2B Q4_K_M:
///   косинусы между сплошными квадратами разного цвета 0.82-0.84 (совпадали бы ~1.0, если бы
///   картинку выбрасывали), а текст «красный квадрат» ближе к красному (0.641), чем к синему
///   (0.535) и зелёному (0.556) — то есть кросс-модальность, ради которой всё и делается, работает.
///
/// Почему не vLLM, на котором визуальная половина была задумана: на карте этого кластера он
/// не запускается в принципе. GTX 1650 SUPER — это Turing (SM 7.5), bf16 там нет, значит fp16,
/// а fp16-веса Qwen3-VL-Embedding-2B весят 4.26 ГБ при 4 ГБ видеопамяти. Плюс nvidia.com/gpu
/// неделим, и второй под с картой всё равно остался бы в Pending рядом с модулем ai.
/// </summary>
internal sealed class HttpVisionEmbeddingGenerator(
    IHttpClientFactory factory,
    SearchOptions options,
    ILogger<HttpVisionEmbeddingGenerator> logger) : IVisionEmbeddingGenerator
{
    public const string HttpClientName = "flow-embeddings-vl";

    /// <summary>
    /// Маркер медиа llama-server выдаёт в <c>/props</c>, и путь именно такой — в корне сервера,
    /// а не под <c>/v1</c>: <c>/v1/props</c> отвечает 404. Ведущий слэш обязателен, иначе адрес
    /// соберётся относительно BaseAddress (он оканчивается на <c>/v1/</c>) и уедет не туда.
    /// </summary>
    private const string PropsPath = "/props";

    private readonly SearchVisionOptions _options = options.Embeddings.Vision;

    /// <summary>
    /// Маркер живёт ровно столько, сколько процесс модели на сервере: он генерируется случайным
    /// при каждом её старте. Кэшируем, потому что за ним ходить на каждую картинку — лишний
    /// round-trip, а протухание ловится ниже по ошибке и стоит одной перезакачки маркера.
    /// </summary>
    private string? _mediaMarker;

    /// <summary>
    /// Версия несёт имя файла весов, а не только имя модели, и это не педантизм: вектор задаёт
    /// конкретный GGUF, а имя модели в роутере остаётся прежним при любой смене квантизации.
    /// Без файла в версии переезд с vLLM на llama.cpp (и любая смена Q4_K_M на Q8_0) оставил бы
    /// в базе старые векторы, несравнимые с новыми, — и поиск по картинкам молча выдавал бы мусор.
    /// С файлом переиндексация запускается сама: поиск читает только текущую версию.
    /// </summary>
    public string ModelVersion { get; } = Qwen3Embeddings.BuildModelVersion(
        options.Embeddings.Vision.Model,
        options.Embeddings.Vision.Dimensions,
        options.Embeddings.Vision.ModelFile);

    public bool IsConfigured => options.VisionEnabled && !string.IsNullOrWhiteSpace(_options.Endpoint);

    /// <summary>
    /// Запрос — обычный текст: маркер и multimodal_data здесь не нужны, а модель одна и та же,
    /// поэтому текст запроса и картинка оказываются в одном векторном пространстве.
    /// </summary>
    public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken) =>
        SendAsync(new { model = _options.Model, input = query }, "запрос", cancellationToken);

    public async Task<float[]> EmbedImageAsync(byte[] image, string contentType, CancellationToken cancellationToken)
    {
        // contentType в теле не участвует: llama-server определяет формат по самим байтам. Параметр
        // остаётся в интерфейсе, потому что вызывающая сторона отсеивает по нему неподдерживаемые типы.
        var payload = Convert.ToBase64String(image);
        var marker = await GetMediaMarkerAsync(cancellationToken);

        try
        {
            return await SendAsync(BuildImageRequest(marker, payload), "картинку", cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Единственная ожидаемая причина отказа на корректной картинке — протухший маркер:
            // процесс модели перезапустился (перезапуск пода, вытеснение по --models-max) и выдал
            // новый. Сервер отвечает на такое 500 «Failed to tokenize prompt», а НЕ молчаливым
            // эмбеддингом текста, — проверено, — поэтому промах ловится, а не превращается в тихо
            // испорченный индекс. Одна повторная попытка со свежим маркером; вторая уже бессмысленна.
            _mediaMarker = null;
            var fresh = await GetMediaMarkerAsync(cancellationToken);

            if (fresh == marker)
                throw;

            logger.LogInformation("Маркер медиа визуальной модели сменился — повторяем запрос с новым.");
            return await SendAsync(BuildImageRequest(fresh, payload), "картинку", cancellationToken);
        }
    }

    /// <summary>
    /// Собственный формат llama-server: картинка едет отдельным полем, а её место в промпте
    /// отмечает маркер. Поле model обязательно — по нему роутер выбирает процесс модели.
    /// </summary>
    private object BuildImageRequest(string marker, string base64) => new
    {
        model = _options.Model,
        input = new object[] { new { prompt_string = marker, multimodal_data = new[] { base64 } } }
    };

    private async Task<string> GetMediaMarkerAsync(CancellationToken cancellationToken)
    {
        if (_mediaMarker is { Length: > 0 } cached)
            return cached;

        var client = factory.CreateClient(HttpClientName);

        // model в query обязателен: в router-режиме /props без него описывает сам роутер, а маркер
        // принадлежит процессу конкретной модели. Побочный эффект запроса полезный — он же и грузит
        // модель, если она ещё не поднята, поэтому первая картинка не ждёт загрузку внутри инференса.
        var response = await client.GetAsync($"{PropsPath}?model={Uri.EscapeDataString(_options.Model)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var props = await response.Content.ReadFromJsonAsync<PropsResponse>(cancellationToken);
        var marker = props?.MediaMarker;

        if (string.IsNullOrWhiteSpace(marker))
            throw new InvalidOperationException(
                $"Визуальная модель {_options.Model} не сообщила media_marker в {PropsPath}. " +
                "Обычно это значит, что в models.ini у секции модели нет ключа mmproj: без проектора " +
                "сервер грузит только текстовую половину и картинку принять не может.");

        _mediaMarker = marker;
        return marker;
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

    private sealed record PropsResponse(
        [property: JsonPropertyName("media_marker")] string? MediaMarker);

    private sealed record EmbeddingsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingItem>? Data);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("embedding")] IReadOnlyList<float>? Embedding);
}
