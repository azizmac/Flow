using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Application.Features.Bootstrap;
using Flow.Infrastructure.DependencyInjection;
using Flow.Infrastructure.Persistence;
using Flow.Infrastructure.Search;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Один Postgres-контейнер (тот же образ, что в docker-compose) на всю коллекцию тестов,
/// миграции применяются один раз. Тесты изолируют данные уникальными ключами досок.
/// Требует запущенного Docker.
///
/// Поиск включён (Search:Enabled=true) с <see cref="FakeEmbeddingGenerator"/>: тесты проверяют очередь
/// и чанки, но модель не тянут. Воркер объявлен выключенным — здесь нет хоста, и запускают его тесты
/// руками через <see cref="RunIndexingAsync"/>, чтобы прогон был предсказуемым, а не «когда-нибудь».
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Owner, который сеется после миграций: actor для команд в интеграционных тестах.</summary>
    public static readonly Guid OwnerId = Guid.Parse("00000000-0000-0000-0000-00000000aaaa");

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private ServiceProvider _services = null!;

    /// <summary>Детерминированный эмбеддер: счётчик вызовов — то, чем проверяется переиспользование векторов.</summary>
    public FakeEmbeddingGenerator Embeddings { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Тот же путь регистрации, что и в Flow.Api/Program.cs — заодно проверяется DI-проводка.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _container.GetConnectionString(),
                ["Search:Enabled"] = "true",
                ["Search:Indexing:Enabled"] = "false",
                ["Search:Indexing:BatchSize"] = "32",
                ["Search:Embeddings:QueryEndpoint"] = "http://embeddings.invalid/v1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlowInfrastructure(configuration);
        services.AddFlowApplication();
        // В Flow.Auth эти тесты не ходят: учётные записи всегда «создаются» успешно.
        services.AddSingleton<IAccountService, AlwaysSucceedingAccountService>();
        // Поверх HttpEmbeddingGenerator: последняя регистрация выигрывает у GetRequiredService.
        services.AddSingleton<IEmbeddingGenerator>(Embeddings);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FlowDbContext>().Database.MigrateAsync();

        await SendAsync(new SeedBootstrapUserCommand(OwnerId, "owner", "owner@example.com", "Owner", "Flow"));
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

    /// <summary>Свой scope для тестов, которым нужна одна транзакция на несколько команд.</summary>
    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    /// <summary>
    /// Один прогон индексации: разбирает очередь до пустоты (но не более <paramref name="passes"/> проходов,
    /// чтобы упавший источник не зациклил тест). Возвращает число обработанных записей.
    /// </summary>
    public async Task<int> RunIndexingAsync(int passes = 10)
    {
        var worker = CreateWorker();
        var processed = 0;

        for (var i = 0; i < passes; i++)
        {
            var batch = await worker.ProcessBatchAsync(CancellationToken.None);
            processed += batch;

            if (batch == 0)
                break;
        }

        return processed;
    }

    /// <summary>Второй воркер на той же очереди — для проверки SKIP LOCKED.</summary>
    internal SearchIndexingWorker CreateWorker() => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        _services.GetRequiredService<SearchOptions>(),
        _services.GetRequiredService<ILogger<SearchIndexingWorker>>());
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
