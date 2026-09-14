using Flow.Auth.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Auth.Tests;

/// <summary>
/// Один Postgres-контейнер и один хост Flow.Api с Auth-модулем (WebApplicationFactory) на всю коллекцию: при старте
/// хост сам применяет миграции обоих контекстов (таблицы ядра — в public, Auth-модуля — в схеме auth) и сеет клиентов
/// и bootstrap-пользователя — тот же путь, что и в Docker. Ключи — эфемерные (Auth:UseEphemeralKeys), чтобы не трогать
/// хранилище сертификатов. Требует Docker.
/// </summary>
public sealed class AuthFixture : IAsyncLifetime
{
    public const string ClientRedirectUri = "http://localhost:5016/authentication/login-callback";
    public static readonly Guid BootstrapId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplicationFactory<Program> Factory { get; set; } = null!;

    private IServiceProvider Services => Factory.Services;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _container.GetConnectionString());
            builder.UseSetting("Auth:Issuer", "http://localhost");
            builder.UseSetting("Auth:UseEphemeralKeys", "true");
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

    /// <summary>Клиент без автоследования редиректам: OIDC-редиректы ведут на внешний redirect_uri, их читаем руками.</summary>
    public HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    /// <summary>Клиент API без cookie: аутентификация возможна только по Bearer-токену.</summary>
    public HttpClient CreateApiClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false
    });

    /// <summary>
    /// Создаёт учётную запись напрямую через Identity. По умолчанию без обязательной смены пароля —
    /// иначе каждый тест входа упирался бы в /account/change-password. Username/email уникальны по имени теста.
    /// </summary>
    public async Task<ApplicationUser> CreateAccountAsync(string username, string password = "correct horse battery", Guid? id = null, bool mustChangePassword = false)
    {
        await using var scope = CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = id ?? Guid.NewGuid(),
            UserName = username.Trim().ToLowerInvariant(),
            Email = $"{username}@example.com",
            EmailConfirmed = true,
            LockoutEnabled = true,
            MustChangePassword = mustChangePassword
        };

        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Description)));
        return user;
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();
}

[CollectionDefinition(Name)]
public sealed class AuthCollection : ICollectionFixture<AuthFixture>
{
    public const string Name = "Auth";
}
