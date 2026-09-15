using Flow.Application.Abstractions;
using Flow.Infrastructure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Flow.Infrastructure.DependencyInjection;

public static class FlowSearchServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует поиск: секцию "Search", эмбеддер по <c>Search:Embeddings:Provider</c>, очередь
    /// индексации и фонового воркера. Вызывается из AddFlowInfrastructure, но оставлен публичным и
    /// идемпотентным: Flow.Api/Program.cs зовёт его явно рядом с AddFlowInfrastructure, а тесты —
    /// чтобы собрать контейнер тем же путём, что и приложение.
    ///
    /// Воркер стартует только при <c>Search:Enabled</c> и <c>Search:Indexing:Enabled</c>: с выключенным
    /// поиском (дефолт в репозитории) в приложении не появляется ни одного лишнего потока.
    /// </summary>
    public static IServiceCollection AddFlowSearch(this IServiceCollection services, IConfiguration configuration)
    {
        // Повторный вызов ничего не добавляет: иначе HttpClient'ы настраивались бы дважды.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(SearchOptions)))
            return services;

        var options = configuration.GetSection(SearchOptions.SectionName).Get<SearchOptions>() ?? new SearchOptions();

        // Размерность зашита в тип столбца halfvec(N): рассинхрон с конфигом дал бы не ошибку схемы,
        // а молча неработающий поиск, поэтому падаем на старте.
        if (options.Enabled && options.Embeddings.Dimensions != SearchSchema.Dimensions)
            throw new InvalidOperationException(
                $"Search:Embeddings:Dimensions = {options.Embeddings.Dimensions}, а схема рассчитана на " +
                $"halfvec({SearchSchema.Dimensions}). Сменить размерность можно только миграцией.");

        services.AddSingleton(options);

        services.AddHttpClient(HttpEmbeddingGenerator.QueryClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.Embeddings.TimeoutSeconds)));

        services.AddHttpClient(HttpEmbeddingGenerator.IndexingClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(HttpEmbeddingGenerator.IndexingTimeoutSeconds));

        services.TryAddSingleton<IEmbeddingGenerator>(provider => options.Embeddings.Provider switch
        {
            EmbeddingProvider.Http => ActivatorUtilities.CreateInstance<HttpEmbeddingGenerator>(provider),
            EmbeddingProvider.Onnx => throw new NotSupportedException(
                "Search:Embeddings:Provider=Onnx пока не реализован: в этой ветке модель крутится сайдкаром " +
                "llama-server (Provider=Http). Абстракция IEmbeddingGenerator готова — нужна только реализация."),
            _ => throw new NotSupportedException($"Неизвестный Search:Embeddings:Provider: {options.Embeddings.Provider}.")
        });

        // Очередь — scoped поверх того же FlowDbContext, что и репозитории: постановка коммитится
        // той же транзакцией, что и правка сущности (см. UnitOfWork).
        services.AddScoped<SearchIndexQueue>();
        services.AddScoped<ISearchIndexQueue>(provider => provider.GetRequiredService<SearchIndexQueue>());

        services.AddScoped<SearchIndexStore>();
        services.AddScoped<ISearchIndexStore>(provider => provider.GetRequiredService<SearchIndexStore>());

        services.AddScoped<SearchSourceReader>();

        if (options is { Enabled: true, Indexing.Enabled: true })
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SearchIndexingWorker>());

        return services;
    }
}
