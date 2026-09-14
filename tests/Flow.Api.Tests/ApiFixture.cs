using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flow.Application.Tests.Fakes;
using Flow.Auth.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Хост Flow.Api (WebApplicationFactory) на Postgres из Testcontainers: при старте сам применяет миграции обоих
/// контекстов (ядро — public, Auth-модуль — схема auth) и сеет bootstrap-профиль — тот же путь, что в Docker.
/// Production OpenIddict Validation заменена тестовой JwtBearer-схемой с локальным симметричным ключом
/// (токены выпускает <see cref="CreateToken"/>); настоящая локальная validation покрыта Flow.Auth.Tests.
/// IAccountService → FakeAccountService. Требует Docker.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Issuer = "http://auth.test";
    public static readonly Guid BootstrapId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly SymmetricSecurityKey SigningKey =
        new("flow-api-tests-signing-key-must-be-at-least-32-bytes"u8.ToArray());

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public FakeAccountService Accounts { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _container.GetConnectionString());
            builder.UseSetting("Auth:Issuer", Issuer);
            builder.UseSetting("Auth:UseEphemeralKeys", "true");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccountService>(Accounts);

                services
                    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.MapInboundClaims = false;
                        options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                        options.TokenValidationParameters.ValidIssuer = Issuer;
                        options.TokenValidationParameters.ValidAudience = "flow-api";
                    });

                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                });
            });
        });

        using var _ = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    public HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>Клиент с Bearer-токеном для пользователя sub = userId (по умолчанию — bootstrap-пользователь).</summary>
    public HttpClient CreateClientAs(Guid? userId = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId ?? BootstrapId));
        return client;
    }

    public static string CreateToken(Guid subject, string audience = "flow-api", string issuer = Issuer) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            TokenType = "at+jwt",
            Subject = new ClaimsIdentity([new Claim("sub", subject.ToString()), new Claim("name", "test")]),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256)
        });

    public AsyncServiceScope CreateScope() => Factory.Services.CreateAsyncScope();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
