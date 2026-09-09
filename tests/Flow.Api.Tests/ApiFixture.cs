using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flow.Application.Abstractions;
using Flow.Application.Tests.Fakes;
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
/// Хост Flow.Api (WebApplicationFactory) на Postgres из Testcontainers: при старте сам применяет миграции и сеет
/// bootstrap-профиль — тот же путь, что в Docker. Flow.Auth не поднимается: JwtBearer переключён на локальный
/// симметричный ключ (токены выпускает <see cref="CreateToken"/>), IAccountService → FakeAccountService. Требует Docker.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Issuer = "http://auth.test";
    public static readonly Guid BootstrapId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flow-api-tests-signing-key-must-be-at-least-32-bytes"));

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public FakeAccountService Accounts { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _container.GetConnectionString());
            builder.UseSetting("Auth:BaseUrl", Issuer);
            builder.UseSetting("Auth:Issuer", Issuer);
            builder.UseSetting("Auth:ApiClient:Secret", "unused");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccountService>(Accounts);

                // Без discovery: проверяем подпись локальным ключом, issuer и audience — как у настоящего Flow.Auth.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.MetadataAddress = null;
                    options.ConfigurationManager = null;
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                    options.TokenValidationParameters.ValidIssuer = Issuer;
                    options.TokenValidationParameters.ValidAudience = "flow-api";
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
