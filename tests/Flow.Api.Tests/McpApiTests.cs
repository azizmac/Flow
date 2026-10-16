using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flow.Shared.Contracts.Boards;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// MCP-сервер (Flow.Api/Mcp) настоящим клиентом SDK поверх тестового хоста. Инструменты — тонкий слой над теми же
/// командами, что у контроллеров, поэтому проверяется то, что есть только здесь: вход по Bearer, разбор имён
/// (ключ проекта, код задачи, имя статуса, username) и что ошибка Application доходит до модели текстом.
/// OAuth-discovery (resource_metadata в 401) проверяет Flow.Auth.Tests: здесь схема подменена на JwtBearer.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class McpApiTests(ApiFixture api)
{
    [Fact]
    public async Task Mcp_Without_Token_Should_Answer_401()
    {
        using var client = api.CreateClient(allowAutoRedirect: false);

        using var response = await client.PostAsync("/mcp", new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tools_Should_Be_Listed()
    {
        using var http = api.CreateClientAs();
        await using var mcp = await ConnectAsync(http);

        var tools = (await mcp.ListToolsAsync()).Select(t => t.Name).ToList();

        Assert.Contains("list_projects", tools);
        Assert.Contains("get_project", tools);
        Assert.Contains("search_tasks", tools);
        Assert.Contains("get_task", tools);
        Assert.Contains("create_task", tools);
        Assert.Contains("update_task", tools);
        Assert.Contains("add_comment", tools);
        Assert.DoesNotContain(tools, t => t.Contains("delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Task_Lifecycle_Should_Work_By_Names()
    {
        using var http = api.CreateClientAs();
        using var created = await http.PostAsJsonAsync("/api/boards", new CreateBoardRequest("MCP board", "MCPA"));
        var board = (await created.Content.ReadFromJsonAsync<BoardResponse>())!;
        var final = board.Statuses.First(s => s.IsFinal);
        await using var mcp = await ConnectAsync(http);

        var task = await CallAsync(mcp, "create_task", new()
        {
            ["project"] = "mcpa",
            ["title"] = "Починить вход",
            ["description"] = "Падает на пустом пароле",
            ["type"] = "bug",
            ["priority"] = "high",
            ["assignee"] = "me"
        });
        var code = task.GetProperty("code").GetString()!;
        Assert.Equal("MCPA-1", code);
        Assert.Equal("High", task.GetProperty("priority").GetString());
        Assert.Equal("MCPA", task.GetProperty("project").GetString());
        Assert.Equal("admin", task.GetProperty("assignee").GetString());

        var page = await CallAsync(mcp, "search_tasks", new() { ["fql"] = "project = MCPA AND assignee = me()" });
        Assert.Equal(1, page.GetProperty("matched").GetInt32());
        Assert.Equal(code, page.GetProperty("items")[0].GetProperty("code").GetString());

        var updated = await CallAsync(mcp, "update_task", new() { ["task"] = code.ToLowerInvariant(), ["status"] = final.Name.ToUpperInvariant() });
        Assert.Equal(final.Name, updated.GetProperty("status").GetString());
        Assert.True(updated.GetProperty("isDone").GetBoolean());

        await CallAsync(mcp, "add_comment", new() { ["task"] = code, ["body"] = "Готово" });
        var details = await CallAsync(mcp, "get_task", new() { ["task"] = code });
        Assert.Equal("Падает на пустом пароле", details.GetProperty("task").GetProperty("description").GetString());
        Assert.Equal("Готово", details.GetProperty("comments")[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task Errors_Should_Reach_The_Model_As_Text()
    {
        using var http = api.CreateClientAs();
        using var created = await http.PostAsJsonAsync("/api/boards", new CreateBoardRequest("MCP errors", "MCPB"));
        await using var mcp = await ConnectAsync(http);
        await CallAsync(mcp, "create_task", new() { ["project"] = "MCPB", ["title"] = "Задача" });

        var unknownStatus = await mcp.CallToolAsync("update_task", new Dictionary<string, object?> { ["task"] = "MCPB-1", ["status"] = "Нет такого" });
        var badFql = await mcp.CallToolAsync("search_tasks", new Dictionary<string, object?> { ["fql"] = "project = = MCPB" });
        var hiddenTask = await mcp.CallToolAsync("get_task", new Dictionary<string, object?> { ["task"] = "NOPE-1" });

        Assert.True(unknownStatus.IsError);
        Assert.Contains("Нет такого", Text(unknownStatus));
        Assert.True(badFql.IsError);
        Assert.Contains("FQL", Text(badFql));
        Assert.True(hiddenTask.IsError);
        Assert.Contains("NOPE-1", Text(hiddenTask));
    }

    private static Task<McpClient> ConnectAsync(HttpClient http) =>
        McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http, ownsHttpClient: false));

    private static async Task<JsonElement> CallAsync(McpClient mcp, string tool, Dictionary<string, object?> arguments)
    {
        var result = await mcp.CallToolAsync(tool, arguments);
        Assert.False(result.IsError == true, Text(result));
        return JsonDocument.Parse(Text(result)).RootElement.Clone();
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
