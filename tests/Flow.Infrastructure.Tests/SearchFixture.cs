using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Application.Features.Bootstrap;
using Flow.Application.Features.Search;
using Flow.Application.Tests.Fakes;
using Flow.Infrastructure.DependencyInjection;
using Flow.Infrastructure.Persistence;
using Flow.Infrastructure.Search;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Контейнер с включённым поиском: очередь наполняется, а проход индексации тесты делают сами
/// (Search:Indexing:Enabled=false — фоновый воркер в тестах не нужен, иначе он разбирал бы очередь
/// из-под них). Эмбеддер — детерминированный фейк: модель не тянется, зато считаются вызовы.
/// </summary>
public sealed class SearchFixture : IAsyncLifetime
{
    public static readonly Guid OwnerId = Guid.Parse("00000000-0000-0000-0000-00000000bbbb");

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private ServiceProvider _services = null!;

    public FakeEmbeddingGenerator Embedder { get; } = new();

    public InMemoryFileStorage Storage { get; } = new();

    /// <summary>Визуальная модель тоже фейковая: проверяется индексация картинок, а не качество модели.</summary>
    public FakeVisionEmbeddingGenerator Vision { get; } = new() { IsConfigured = true };

    /// <summary>Тот же синглтон настроек, что читают хендлеры: тестам он нужен, чтобы гасить модели.</summary>
    public SearchOptions Options => _services.GetRequiredService<SearchOptions>();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _container.GetConnectionString(),
                ["Search:Enabled"] = "true",
                ["Search:Indexing:Enabled"] = "false",
                ["Search:Indexing:BatchSize"] = "32",
                ["Search:Indexing:MaxAttempts"] = "3",
                ["Search:Embeddings:QueryEndpoint"] = "http://localhost:1/v1",
                ["Search:Embeddings:Vision:Enabled"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // До AddFlowInfrastructure: там эмбеддер регистрируется через TryAdd, и фейк остаётся за нами.
        services.AddSingleton<IEmbeddingGenerator>(Embedder);
        services.AddSingleton<IVisionEmbeddingGenerator>(Vision);
        services.AddFlowInfrastructure(configuration);
        services.AddFlowApplication();
        services.AddSingleton<IAccountService, AlwaysSucceedingAccountService>();
        // Вложения кладём в память: S3-клиент проверяется отдельным тестом против MinIO.
        services.AddSingleton<IFileStorage>(Storage);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await FlowDatabase.MigrateAsync(scope.ServiceProvider, CancellationToken.None);

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

    /// <summary>Один проход индексации — то же, что делает фоновый воркер, но по команде теста.</summary>
    public async Task<int> RunIndexingAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SearchIndexingRunner>().RunOnceAsync(CancellationToken.None);
    }

    /// <summary>
    /// Разбирает очередь до конца: за проход берётся не больше BatchSize записей. Застрявшую запись
    /// поднимает исключением — иначе тест падал бы на пустом индексе, не показав причину.
    /// </summary>
    public async Task DrainIndexingAsync()
    {
        while (await RunIndexingAsync() > 0)
        {
        }

        var failed = await QueryAsync(db => db.SearchIndexQueue
            .Where(r => r.LastError != null)
            .Select(r => r.SourceType + " " + r.SourceId + ": " + r.LastError)
            .ToListAsync());

        if (failed.Count > 0)
            throw new InvalidOperationException("Индексация не справилась: " + string.Join("; ", failed));
    }

    /// <summary>Убирает записи очереди после тестов, которые намеренно ломали индексацию.</summary>
    public Task ClearQueueAsync() => QueryAsync(db => db.SearchIndexQueue.ExecuteDeleteAsync());

    /// <summary>Scope целиком — для тестов, которым нужна своя транзакция вокруг команды.</summary>
    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();
}

[CollectionDefinition(Name)]
public sealed class SearchCollection : ICollectionFixture<SearchFixture>
{
    public const string Name = "Search";
}
