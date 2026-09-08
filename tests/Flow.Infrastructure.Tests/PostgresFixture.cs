using Flow.Application.DependencyInjection;
using Flow.Infrastructure.DependencyInjection;
using Flow.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Один Postgres-контейнер (тот же образ, что в docker-compose) на всю коллекцию тестов,
/// миграции применяются один раз. Тесты изолируют данные уникальными ключами досок.
/// Требует запущенного Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Тот же путь регистрации, что и в Flow.Api/Program.cs — заодно проверяется DI-проводка.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _container.GetConnectionString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFlowInfrastructure(configuration);
        services.AddFlowApplication();
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FlowDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Каждый запрос — в своём scope (свой DbContext), как отдельный HTTP-запрос в Flow.Api.</summary>
    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, CancellationToken.None);
    }

    /// <summary>Прямой доступ к БД для ассертов, в отдельном scope, без трекинга.</summary>
    public async Task<TResult> QueryAsync<TResult>(Func<FlowDbContext, Task<TResult>> query)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDbContext>();
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return await query(db);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
