using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flow.Shared.Contracts.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Auth.Tests;

/// <summary>
/// Один Postgres-контейнер и один хост Flow.Auth (WebApplicationFactory) на всю коллекцию: при старте хост сам
/// применяет миграции, сеет клиентов и bootstrap-пользователя — то есть проверяется тот же путь, что и в Docker.
/// Ключи — эфемерные (Auth:UseEphemeralKeys), чтобы не трогать хранилище сертификатов. Требует Docker.
/// </summary>
public sealed class AuthFixture : IAsyncLifetime
{
    public const string ApiClientSecret = "test-secret";
    public const string ClientRedirectUri = "http://localhost:5016/authentication/login-callback";
    public static readonly Guid BootstrapId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public IServiceProvider Services => Factory.Services;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _container.GetConnectionString());
            builder.UseSetting("Auth:Issuer", "http://localhost");
            builder.UseSetting("Auth:UseEphemeralKeys", "true");
            builder.UseSetting("Auth:ApiClient:Secret", ApiClientSecret);
            builder.UseSetting("Auth:Client:RedirectUris:0", ClientRedirectUri);
            builder.UseSetting("Auth:Client:PostLogoutRedirectUris:0", "http://localhost:5016/authentication/logout-callback");
        });

        // Первый клиент поднимает хост (миграции + сидеры).
        using var _ = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Клиент без автоследования редиректов: OIDC-редиректы ведут на внешний redirect_uri, их читаем руками.</summary>
    public HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    /// <summary>Клиент с Bearer-токеном flow-api (client_credentials, scope auth:admin) для admin-API.</summary>
    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();
        var token = await GetClientCredentialsTokenAsync(client, "flow-api", ApiClientSecret, "auth:admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<string> GetClientCredentialsTokenAsync(HttpClient client, string clientId, string secret, string scope)
    {
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["scope"] = scope
        }));
        response.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>Создаёт учётную запись через admin-API. Username/email уникальны по имени теста.</summary>
    public async Task<AccountResponse> CreateAccountAsync(string username, string password = "correct horse battery", Guid? id = null)
    {
        using var admin = await CreateAdminClientAsync();
        using var response = await admin.PostAsJsonAsync("/accounts", new CreateAccountRequest(id ?? Guid.NewGuid(), username, $"{username}@example.com", password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();
}

[CollectionDefinition(Name)]
public sealed class AuthCollection : ICollectionFixture<AuthFixture>
{
    public const string Name = "Auth";
}
