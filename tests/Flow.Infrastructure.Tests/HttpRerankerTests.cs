using System.Net;
using System.Text;
using System.Text.Json;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// HTTP-клиент второй ступени на заглушке: ни сети, ни модели. Проверяется контракт с сервисом —
/// что уходит в запросе и как разбирается ответ, включая расхождение в имени поля оценки
/// (llama.cpp и Jina отдают relevance_score, часть сборок — score).
/// </summary>
public class HttpRerankerTests
{
    private static SearchOptions Options() => new()
    {
        Enabled = true,
        Rerank = new SearchRerankOptions
        {
            Enabled = true,
            Endpoint = "http://reranker:8082/v1",
            Model = "Qwen3-Reranker-0.6B"
        }
    };

    private static HttpReranker Create(StubHandler handler, SearchOptions? options = null) =>
        new(new StubHttpClientFactory(handler), options ?? Options(), NullLogger<HttpReranker>.Instance);

    [Fact]
    public async Task Request_Carries_Model_Query_And_Documents()
    {
        var handler = new StubHandler("""{"results":[{"index":0,"relevance_score":0.9}]}""");

        await Create(handler).RankAsync("падает экспорт", ["первый документ"], CancellationToken.None);

        Assert.Equal("http://reranker/v1/rerank", handler.Url);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Qwen3-Reranker-0.6B", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("падает экспорт", body.RootElement.GetProperty("query").GetString());
        Assert.Equal("первый документ", body.RootElement.GetProperty("documents")[0].GetString());
    }

    [Fact]
    public async Task Scores_Come_Back_By_Index()
    {
        var handler = new StubHandler("""{"results":[{"index":1,"relevance_score":0.81},{"index":0,"relevance_score":0.12}]}""");

        var scores = await Create(handler).RankAsync("запрос", ["первый", "второй"], CancellationToken.None);

        Assert.Equal(2, scores.Count);
        Assert.Equal(0.81, scores.First(score => score.Index == 1).Score, 3);
        Assert.Equal(0.12, scores.First(score => score.Index == 0).Score, 3);
    }

    [Fact]
    public async Task Score_Field_Is_Accepted_Under_Both_Names()
    {
        var handler = new StubHandler("""{"results":[{"index":0,"score":0.55}]}""");

        var scores = await Create(handler).RankAsync("запрос", ["документ"], CancellationToken.None);

        Assert.Equal(0.55, Assert.Single(scores).Score, 3);
    }

    [Fact]
    public async Task Indexes_Outside_The_Input_Are_Dropped()
    {
        // Защита от сервиса, который вернул больше, чем ему прислали: выдача пользователя от этого падать не должна.
        var handler = new StubHandler("""{"results":[{"index":0,"relevance_score":0.9},{"index":7,"relevance_score":0.99}]}""");

        var scores = await Create(handler).RankAsync("запрос", ["единственный"], CancellationToken.None);

        Assert.Equal(0, Assert.Single(scores).Index);
    }

    [Fact]
    public async Task Server_Error_Becomes_An_Exception_With_The_Body()
    {
        var handler = new StubHandler("""{"error":"model not loaded"}""") { Status = HttpStatusCode.ServiceUnavailable };

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Create(handler).RankAsync("запрос", ["документ"], CancellationToken.None));

        // Текст нужен в логе: «503» без тела не отличает погашенный сервис от неверной модели.
        Assert.Contains("model not loaded", error.Message);
    }

    [Fact]
    public async Task Without_An_Endpoint_The_Stage_Is_Not_Configured()
    {
        var options = Options();
        options.Rerank.Endpoint = string.Empty;
        var handler = new StubHandler("""{"results":[]}""");

        var reranker = Create(handler, options);

        Assert.False(reranker.IsConfigured);
        Assert.Empty(await reranker.RankAsync("запрос", ["документ"], CancellationToken.None));
        Assert.Null(handler.Url);
    }

    [Fact]
    public async Task Empty_Document_List_Does_Not_Reach_The_Model()
    {
        var handler = new StubHandler("""{"results":[]}""");

        Assert.Empty(await Create(handler).RankAsync("запрос", [], CancellationToken.None));
        Assert.Null(handler.Url);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://reranker/v1/") };
    }

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public string? Url { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
