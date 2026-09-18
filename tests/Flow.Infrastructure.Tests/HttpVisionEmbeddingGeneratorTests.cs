using System.Net;
using System.Text;
using System.Text.Json;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Клиент визуальной модели на заглушке: ни сети, ни весов. Проверяется ровно то, что нельзя увидеть
/// в коде глазами и что уже один раз стоило проекту неверного вывода, — форма запроса.
///
/// Прежняя реализация слала vLLM-форму (messages с image_url), llama-server её на эндпоинте
/// эмбеддингов не разбирает вовсе и молча эмбеддит текстовый остаток. Ошибка без единой записи
/// в логе: вектор приходит, размерность правильная, а картинки в нём нет. Поэтому тесты ниже
/// проверяют не «ответ разобрался», а что в тело уехали и маркер медиа, и сами байты.
/// </summary>
public class HttpVisionEmbeddingGeneratorTests
{
    private const string Marker = "<__media_TESTMARKER__>";

    private static readonly byte[] Image = [0x89, 0x50, 0x4E, 0x47];

    private static SearchOptions Options(string modelFile = "Qwen3-VL-Embedding-2B.Q4_K_M.gguf") => new()
    {
        Enabled = true,
        Embeddings = new SearchEmbeddingsOptions
        {
            Vision = new SearchVisionOptions
            {
                Enabled = true,
                Endpoint = "http://ai:8081/v1",
                Model = "Qwen3-VL-Embedding-2B",
                ModelFile = modelFile,
                Dimensions = 4
            }
        }
    };

    private static HttpVisionEmbeddingGenerator Create(StubHandler handler, SearchOptions? options = null) =>
        new(new StubHttpClientFactory(handler), options ?? Options(), NullLogger<HttpVisionEmbeddingGenerator>.Instance);

    [Fact]
    public async Task Image_Request_Carries_The_Media_Marker_And_The_Bytes()
    {
        var handler = new StubHandler();

        await Create(handler).EmbedImageAsync(Image, "image/png", CancellationToken.None);

        // Маркер спрашивают в корне сервера, а не под /v1: /v1/props отвечает 404.
        Assert.Equal("http://ai:8081/props?model=Qwen3-VL-Embedding-2B", handler.Requests[0].Url);
        Assert.Equal("http://ai:8081/v1/embeddings", handler.Requests[1].Url);

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("Qwen3-VL-Embedding-2B", body.RootElement.GetProperty("model").GetString());

        var item = body.RootElement.GetProperty("input")[0];
        Assert.Equal(Marker, item.GetProperty("prompt_string").GetString());
        Assert.Equal(Convert.ToBase64String(Image), item.GetProperty("multimodal_data")[0].GetString());
    }

    [Fact]
    public async Task Text_Query_Goes_Without_Marker_And_Without_Props()
    {
        var handler = new StubHandler();

        await Create(handler).EmbedQueryAsync("красный квадрат", CancellationToken.None);

        // Одна-единственная запись: за маркером для текста ходить незачем.
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://ai:8081/v1/embeddings", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("красный квадрат", body.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public async Task Marker_Is_Fetched_Once_For_Many_Images()
    {
        var handler = new StubHandler();
        var generator = Create(handler);

        await generator.EmbedImageAsync(Image, "image/png", CancellationToken.None);
        await generator.EmbedImageAsync(Image, "image/png", CancellationToken.None);

        Assert.Equal(1, handler.Requests.Count(request => request.Url.Contains("/props")));
    }

    [Fact]
    public async Task Stale_Marker_Is_Refetched_And_The_Request_Repeated()
    {
        // Сервер перезапустил процесс модели между картинками: старый маркер он больше не знает
        // и отвечает 500 «Failed to tokenize prompt» — именно отказом, а не эмбеддингом текста.
        var handler = new StubHandler { MarkerAfterRestart = "<__media_NEWMARKER__>" };
        var generator = Create(handler);

        await generator.EmbedImageAsync(Image, "image/png", CancellationToken.None);
        handler.RestartModel();
        await generator.EmbedImageAsync(Image, "image/png", CancellationToken.None);

        var embeddings = handler.Requests.Where(request => request.Url.EndsWith("/embeddings")).ToArray();
        Assert.Equal(3, embeddings.Length);

        // Третья попытка — уже с новым маркером, и она успешна.
        using var retried = JsonDocument.Parse(embeddings[^1].Body!);
        Assert.Equal("<__media_NEWMARKER__>", retried.RootElement.GetProperty("input")[0].GetProperty("prompt_string").GetString());
    }

    [Fact]
    public async Task Failure_Without_A_New_Marker_Is_Not_Retried()
    {
        // Маркер тот же — значит причина отказа другая, и повтор только удвоил бы нагрузку.
        var handler = new StubHandler { FailEmbeddings = true };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Create(handler).EmbedImageAsync(Image, "image/png", CancellationToken.None));

        Assert.Equal(1, handler.Requests.Count(request => request.Url.EndsWith("/embeddings")));
    }

    [Fact]
    public void Model_Version_Follows_The_Weights_File()
    {
        // Имя модели в роутере переживает смену квантизации, а вектор — нет. Если версия не поедет
        // за файлом, старые векторы останутся в базе рядом с новыми и молча испортят выдачу.
        var handler = new StubHandler();

        var q4 = Create(handler, Options("Qwen3-VL-Embedding-2B.Q4_K_M.gguf")).ModelVersion;
        var q8 = Create(handler, Options("Qwen3-VL-Embedding-2B.Q8_0.gguf")).ModelVersion;

        Assert.NotEqual(q4, q8);
        Assert.StartsWith("Qwen3-VL-Embedding-2B:4:", q4);
    }

    [Fact]
    public async Task Missing_Projector_Is_Reported_As_Such()
    {
        // Без ключа mmproj сервер поднимает текстовую половину модели и media_marker не отдаёт.
        // Диагноз должен называть причину, а не «пустой ответ»: искать её пришлось бы в пресете.
        var handler = new StubHandler { NoMarker = true };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(handler).EmbedImageAsync(Image, "image/png", CancellationToken.None));

        Assert.Contains("mmproj", error.Message);
    }

    private sealed record CapturedRequest(string Url, string? Body);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://ai:8081/v1/") };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<CapturedRequest> _requests = [];
        private string _marker = Marker;
        private bool _restarted;

        public IReadOnlyList<CapturedRequest> Requests => _requests;

        /// <summary>Маркер, который сервер начнёт отдавать после перезапуска процесса модели.</summary>
        public string? MarkerAfterRestart { get; init; }

        /// <summary>Отказ, не связанный с маркером: повторять такой запрос бессмысленно.</summary>
        public bool FailEmbeddings { get; init; }

        /// <summary>Пресет без mmproj: модель поднялась, но картинку принять нечем.</summary>
        public bool NoMarker { get; init; }

        public void RestartModel()
        {
            _restarted = true;
            _marker = MarkerAfterRestart!;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            _requests.Add(new CapturedRequest(url, body));

            if (url.Contains("/props"))
            {
                var json = NoMarker ? """{"modalities":{"vision":false}}""" : $$"""{"media_marker":"{{_marker}}"}""";
                return Json(HttpStatusCode.OK, json);
            }

            if (FailEmbeddings)
                return Json(HttpStatusCode.InternalServerError, """{"error":{"message":"boom"}}""");

            // Картинка с маркером, который сервер больше не знает: ровно так отвечает llama-server.
            // Маркер достаём из разобранного JSON, а не подстрокой: System.Text.Json экранирует
            // «<» и «>» в < и >, и наивное сравнение с сырым маркером не совпало бы никогда.
            // На проводе это безразлично — экранированная форма валидна и разбирается обратно, — но
            // заглушка обязана смотреть на то же значение, что увидит сервер.
            if (_restarted && SentMarker(body) is { } sent && sent != _marker)
                return Json(HttpStatusCode.InternalServerError, """{"error":{"message":"Failed to tokenize prompt"}}""");

            return Json(HttpStatusCode.OK, """{"data":[{"embedding":[1.0,0.0,0.0,0.0,0.0,0.0,0.0,0.0]}]}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        /// <summary>Маркер из тела запроса, или null, если запрос не про картинку.</summary>
        private static string? SentMarker(string? body)
        {
            if (body is null)
                return null;

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Array)
                return null;

            return input[0].TryGetProperty("prompt_string", out var marker) ? marker.GetString() : null;
        }
    }
}
