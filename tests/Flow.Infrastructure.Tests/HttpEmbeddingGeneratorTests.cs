using System.Net;
using System.Text;
using System.Text.Json;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// HTTP-клиент эмбеддера на заглушке HttpMessageHandler: ни сети, ни модели. Проверяется то,
/// что видит сервер (тело запроса) и что уезжает в индекс (урезанный и нормализованный вектор).
/// </summary>
public class HttpEmbeddingGeneratorTests
{
    private const string Instruction = "Given a search query, retrieve relevant tasks";

    private static SearchOptions Options(int dimensions = 512, int batchSize = 16) => new()
    {
        Enabled = true,
        Embeddings = new SearchEmbeddingsOptions
        {
            Model = "Qwen3-Embedding-0.6B",
            Dimensions = dimensions,
            QueryInstruction = Instruction,
            BatchSize = batchSize
        }
    };

    private static HttpEmbeddingGenerator Create(StubHandler handler, SearchOptions? options = null) =>
        new(new StubHttpClientFactory(handler), options ?? Options(), NullLogger<HttpEmbeddingGenerator>.Instance);

    [Fact]
    public async Task EmbedQuery_Wraps_Query_With_Instruction()
    {
        var handler = new StubHandler(input => input.Select(_ => Ramp(1024)).ToArray());

        await Create(handler).EmbedQueryAsync("экспорт падает", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://embeddings/v1/embeddings", request.Url);
        Assert.Equal("Qwen3-Embedding-0.6B", request.Model);
        Assert.Equal($"Instruct: {Instruction}\nQuery: экспорт падает", Assert.Single(request.Input));
    }

    [Fact]
    public async Task EmbedDocuments_Sends_Text_As_Is()
    {
        var handler = new StubHandler(input => input.Select(_ => Ramp(1024)).ToArray());

        await Create(handler).EmbedDocumentsAsync(["[FLW-1] Экспорт падает"], CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        // Документы уходят без инструкции — асимметрия запроса и документа у Qwen3 обязательна.
        Assert.Equal("[FLW-1] Экспорт падает", Assert.Single(request.Input));
    }

    [Fact]
    public async Task EmbedDocuments_Splits_Into_Batches()
    {
        var handler = new StubHandler(input => input.Select(_ => Ramp(1024)).ToArray());
        var texts = Enumerable.Range(0, 5).Select(i => $"чанк {i}").ToArray();

        var vectors = await Create(handler, Options(batchSize: 2)).EmbedDocumentsAsync(texts, CancellationToken.None);

        Assert.Equal(5, vectors.Count);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([2, 2, 1], handler.Requests.Select(r => r.Input.Count).ToArray());
    }

    [Fact]
    public async Task Response_Is_Truncated_To_Configured_Dimensions_And_Normalized()
    {
        var handler = new StubHandler(input => input.Select(_ => Ramp(1024)).ToArray());

        var vector = await Create(handler).EmbedQueryAsync("запрос", CancellationToken.None);

        Assert.Equal(512, vector.Length);
        Assert.Equal(1.0, Qwen3Embeddings.Norm(vector), Qwen3Embeddings.NormTolerance);
    }

    [Fact]
    public async Task Server_Error_Becomes_Readable_Exception()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("not used")) { Status = HttpStatusCode.ServiceUnavailable };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(handler).EmbedQueryAsync("запрос", CancellationToken.None));

        Assert.Contains("503", error.Message);
    }

    [Fact]
    public async Task IsAvailable_Is_False_When_Server_Is_Down()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        // Погашенная модель — деградация поиска, а не исключение наружу.
        Assert.False(await Create(handler).IsAvailableAsync(CancellationToken.None));
    }

    [Fact]
    public void ModelVersion_Is_Built_From_Options()
    {
        var options = Options();

        var generator = Create(new StubHandler(input => input.Select(_ => Ramp(1024)).ToArray()), options);

        Assert.Equal(
            Qwen3Embeddings.BuildModelVersion(options.Embeddings.Model, options.Embeddings.Dimensions, Instruction),
            generator.ModelVersion);
        Assert.Equal(512, generator.Dimensions);
    }

    private static float[] Ramp(int length) => Enumerable.Range(1, length).Select(i => (float)i).ToArray();

    private sealed record CapturedRequest(string Url, string Model, IReadOnlyList<string> Input);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://embeddings/v1/") };
    }

    private sealed class StubHandler(Func<IReadOnlyList<string>, float[][]> embeddings) : HttpMessageHandler
    {
        private readonly List<CapturedRequest> _requests = [];

        public IReadOnlyList<CapturedRequest> Requests => _requests;

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var payload = JsonDocument.Parse(body).RootElement;
            var input = payload.GetProperty("input").EnumerateArray().Select(item => item.GetString()!).ToArray();

            _requests.Add(new CapturedRequest(request.RequestUri!.ToString(), payload.GetProperty("model").GetString()!, input));

            if (Status != HttpStatusCode.OK)
                return new HttpResponseMessage(Status) { Content = new StringContent("модель не загружена") };

            var data = embeddings(input)
                .Select((vector, index) => new { index, embedding = vector })
                .ToArray();

            var json = JsonSerializer.Serialize(new { data });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
