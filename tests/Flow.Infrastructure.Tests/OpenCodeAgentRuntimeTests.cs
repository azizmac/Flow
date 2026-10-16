using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Flow.Application.Abstractions;
using Flow.Infrastructure.OpenCode;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Конфигурация OpenCode через его HTTP API на записанных ответах (форма сверена с OpenAPI сервера, GET /doc):
/// ключ — PUT/DELETE /auth/{id}, кастомный провайдер — PATCH /global/config, лишние — в disabled_providers.
/// Живой OpenCode в тестах не нужен.
/// </summary>
public class OpenCodeAgentRuntimeTests
{
    private sealed class Recorder(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return respond(request, body);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://opencode:4096/") };
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static OpenCodeAgentRuntime Runtime(Recorder recorder) =>
        new(new Factory(recorder), new OpenCodeOptions(), NullLogger<OpenCodeAgentRuntime>.Instance);

    [Fact]
    public async Task Apply_Sends_Keys_Custom_Providers_And_Disables_The_Stale_Ones()
    {
        var recorder = new Recorder((request, _) => (request.Method.Method, request.RequestUri!.AbsolutePath) switch
        {
            ("GET", "/global/config") =>
                Json("""{"provider":{"old-llm":{"name":"Old"}},"disabled_providers":["gone"]}"""),
            ("GET", "/api/integration/anthropic") =>
                Json("""{"data":{"id":"anthropic","connections":[{"type":"credential","id":"cred_1","label":"Flow"},{"type":"credential","id":"cred_2","label":"manual"}]}}"""),
            ("GET", "/api/integration/deleted") => Json("""{"data":null}"""),
            ("GET", "/api/provider") => Json("""{"data":[{"id":"openrouter"}]}"""),
            _ => Json("true")
        });

        var result = await Runtime(recorder).ApplyAsync(
        [
            new AgentProviderConfig("openrouter", "OpenRouter", false, null, [], "sk-or-1"),
            new AgentProviderConfig("anthropic", "Anthropic", false, null, [], null),
            new AgentProviderConfig("my-llm", "Мой", true, "http://host.docker.internal:1234/v1", [("qwen", "Qwen")], "sk-local")
        ], ["deleted"], CancellationToken.None);

        Assert.All(result.Values, outcome => Assert.Null(outcome.Error));
        Assert.False(result["openrouter"].RestartRequired);
        // Кастомный провайдер новый — сессии OpenCode увидят его только после перезапуска.
        Assert.True(result["my-llm"].RestartRequired);

        var patch = recorder.Requests.Single(r => r.Method == HttpMethod.Patch);
        Assert.Equal("/global/config", patch.Path);
        var body = JsonNode.Parse(patch.Body!)!;
        Assert.Equal(["deleted", "gone", "old-llm"], body["disabled_providers"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("@ai-sdk/openai-compatible", body["provider"]!["my-llm"]!["npm"]!.GetValue<string>());
        Assert.Equal("http://host.docker.internal:1234/v1", body["provider"]!["my-llm"]!["options"]!["baseURL"]!.GetValue<string>());
        Assert.Equal("sk-local", body["provider"]!["my-llm"]!["options"]!["apiKey"]!.GetValue<string>());
        Assert.Equal("Qwen", body["provider"]!["my-llm"]!["models"]!["qwen"]!["name"]!.GetValue<string>());
        Assert.Null(body["provider"]!["openrouter"]);

        // Ключ провайдера из каталога — в учётные данные V2 под меткой Flow.
        var connect = recorder.Requests.Single(r => r.Path == "/api/integration/openrouter/connect/key");
        Assert.Equal("""{"key":"sk-or-1","label":"Flow"}""", connect.Body);

        // Без ключа во Flow снимаются только учётные данные Flow; чужие (manual) не трогаются.
        Assert.Equal(["/api/credential/cred_1"], recorder.Requests.Where(r => r.Method == HttpMethod.Delete && r.Path.StartsWith("/api/credential/")).Select(r => r.Path));
        Assert.Contains(recorder.Requests, r => r.Method == HttpMethod.Delete && r.Path == "/auth/deleted");
    }

    [Fact]
    public async Task Unchanged_Custom_Provider_Seen_By_Sessions_Needs_No_Restart()
    {
        var recorder = new Recorder((request, _) => (request.Method.Method, request.RequestUri!.AbsolutePath) switch
        {
            ("GET", "/global/config") => Json("""
                {"provider":{"my-llm":{"name":"Мой","npm":"@ai-sdk/openai-compatible",
                  "options":{"baseURL":"https://llm.example.com/v1","apiKey":"k"},
                  "models":{"qwen":{"name":"Qwen"},"old":{"name":"Old"}}}}}
                """),
            ("GET", "/api/provider") => Json("""{"data":[{"id":"my-llm"}]}"""),
            _ => Json("true")
        });

        var result = await Runtime(recorder).ApplyAsync(
            [new AgentProviderConfig("my-llm", "Мой", true, "https://llm.example.com/v1", [("qwen", "Qwen")], "k")], [], CancellationToken.None);

        Assert.False(result["my-llm"].RestartRequired);
    }

    [Fact]
    public async Task Cleared_Custom_Key_Is_Overwritten_With_Empty()
    {
        var recorder = new Recorder((request, _) => (request.Method.Method, request.RequestUri!.AbsolutePath) switch
        {
            ("GET", "/global/config") => Json("""{"provider":{"my-llm":{"options":{"baseURL":"https://llm.example.com/v1","apiKey":"old"}}}}"""),
            ("GET", "/api/provider") => Json("""{"data":[{"id":"my-llm"}]}"""),
            _ => Json("true")
        });

        var result = await Runtime(recorder).ApplyAsync(
            [new AgentProviderConfig("my-llm", "Мой", true, "https://llm.example.com/v1", [("qwen", "Qwen")], null)], [], CancellationToken.None);

        var body = JsonNode.Parse(recorder.Requests.Single(r => r.Method == HttpMethod.Patch).Body!)!;
        Assert.Equal("", body["provider"]!["my-llm"]!["options"]!["apiKey"]!.GetValue<string>());
        Assert.True(result["my-llm"].RestartRequired);
    }

    [Fact]
    public async Task Server_Without_V2_Integrations_Gets_The_Key_The_Old_Way()
    {
        var recorder = new Recorder((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/integration/openrouter/connect/key" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/global/config" when request.Method == HttpMethod.Get => Json("{}"),
            _ => Json("true")
        });

        var result = await Runtime(recorder).ApplyAsync(
            [new AgentProviderConfig("openrouter", "OpenRouter", false, null, [], "sk")], [], CancellationToken.None);

        Assert.Null(result["openrouter"].Error);
        var put = recorder.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.Equal("/auth/openrouter", put.Path);
        Assert.Equal("""{"type":"api","key":"sk"}""", put.Body);
    }

    [Fact]
    public async Task Rejected_V1_Config_Is_Retried_In_V2_Form()
    {
        var recorder = new Recorder((request, body) => request.Method == HttpMethod.Patch && body!.Contains("\"provider\"")
            ? Json("""{"name":"BadRequest"}""", HttpStatusCode.BadRequest)
            : request.Method == HttpMethod.Get ? Json("{}") : Json("true"));

        var result = await Runtime(recorder).ApplyAsync(
            [new AgentProviderConfig("my-llm", "Мой", true, "https://llm.example.com/v1", [("qwen", "Qwen")], null)], [], CancellationToken.None);

        Assert.Null(result["my-llm"].Error);
        Assert.True(result["my-llm"].RestartRequired);
        var retry = JsonNode.Parse(recorder.Requests.Where(r => r.Method == HttpMethod.Patch).Last().Body!)!;
        Assert.Equal("@opencode/ai/providers/openai-compatible", retry["providers"]!["my-llm"]!["package"]!.GetValue<string>());
        Assert.Equal("qwen", retry["providers"]!["my-llm"]!["models"]!["qwen"]!["modelID"]!.GetValue<string>());
    }

    [Fact]
    public async Task Rejected_Key_Is_Reported_Per_Provider()
    {
        var recorder = new Recorder((request, _) => request.Method == HttpMethod.Post
            ? Json("""{"message":"bad"}""", HttpStatusCode.BadRequest)
            : request.Method == HttpMethod.Get ? Json("{}") : Json("true"));

        var result = await Runtime(recorder).ApplyAsync(
            [new AgentProviderConfig("openrouter", "OpenRouter", false, null, [], "sk")], [], CancellationToken.None);

        Assert.StartsWith("OpenCode не смог принять ключ: 400", result["openrouter"].Error);
    }

    [Fact]
    public async Task Wrong_Server_Password_Is_Explained()
    {
        var recorder = new Recorder((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Runtime(recorder).ApplyAsync([], [], CancellationToken.None));

        Assert.Contains("OPENCODE_SERVER_PASSWORD", ex.Message);
    }

    [Fact]
    public async Task State_Lists_Connected_Providers_And_Survives_Outage()
    {
        var up = new Recorder((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/global/health" => Json("""{"healthy":true,"version":"2.0.0"}"""),
            _ => Json("""{"data":[{"id":"openrouter"},{"id":"lmstudio"}]}""")
        });
        var state = await Runtime(up).GetStateAsync(CancellationToken.None);
        Assert.True(state.Reachable);
        Assert.Equal("2.0.0", state.Version);
        Assert.Equal(["lmstudio", "openrouter"], state.Connected.Order());

        var down = new Recorder((_, _) => throw new HttpRequestException("Connection refused"));
        var outage = await Runtime(down).GetStateAsync(CancellationToken.None);
        Assert.False(outage.Reachable);
        Assert.Contains("Connection refused", outage.Error);
    }

    [Fact]
    public async Task Catalog_Is_Read_From_Provider_List()
    {
        var recorder = new Recorder((_, _) => Json("""
            {"all":[{"id":"openrouter","name":"OpenRouter","env":["OPENROUTER_API_KEY"],
                     "models":{"openai/gpt-4o-mini":{"id":"openai/gpt-4o-mini","name":"GPT-4o mini"}}}],
             "default":{},"connected":[]}
            """));

        var catalog = await Runtime(recorder).GetCatalogAsync(CancellationToken.None);

        var entry = Assert.Single(catalog);
        Assert.Equal(["OPENROUTER_API_KEY"], entry.Env);
        Assert.Equal(("openai/gpt-4o-mini", "GPT-4o mini"), entry.Models.Single());
    }

    [Fact]
    public async Task Check_Runs_A_Locked_Down_Session_On_The_Given_Model()
    {
        var recorder = new Recorder((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/session" => Json("""{"data":{"id":"ses_1"}}"""),
            "/api/session/ses_1/message" => Json("""{"data":[{"content":[{"type":"text","text":"ok"}]}]}"""),
            _ => Json("{}")
        });

        Assert.Equal("ok", await Runtime(recorder).CheckAsync(new AgentModelRef("openrouter", "openai/gpt-4o-mini"), CancellationToken.None));

        var session = JsonNode.Parse(recorder.Requests.First().Body!)!;
        Assert.Equal("openrouter", session["model"]!["providerID"]!.GetValue<string>());
        Assert.Equal("plan", session["agent"]!.GetValue<string>());
        Assert.Equal("deny", session["permissions"]![0]!["effect"]!.GetValue<string>());
    }

    [Fact]
    public async Task Check_Reports_Provider_Error_From_The_Message()
    {
        var recorder = new Recorder((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/session" => Json("""{"data":{"id":"ses_1"}}"""),
            "/api/session/ses_1/message" => Json("""{"data":[{"content":[],"error":{"data":{"message":"invalid x-api-key"}}}]}"""),
            _ => Json("{}")
        });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Runtime(recorder).CheckAsync(new AgentModelRef("anthropic", "claude"), CancellationToken.None));
        Assert.Contains("invalid x-api-key", ex.Message);
    }
}
