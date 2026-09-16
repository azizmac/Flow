using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Search;
using Flow.Infrastructure.Search.Extraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flow.Infrastructure.DependencyInjection;

public static class FlowSearchServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует поисковую часть: секцию "Search", эмбеддер за <see cref="IEmbeddingGenerator"/>,
    /// очередь индексации и — при Search:Enabled и Search:Indexing:Enabled — фоновый воркер.
    /// Вызывается из <see cref="FlowInfrastructureServiceCollectionExtensions.AddFlowInfrastructure"/>
    /// и повторно из Flow.Api/Program.cs (второй вызов ничего не делает): очередь нужна хендлерам
    /// Application всегда, а тесты поднимают инфраструктуру без Program.cs.
    /// </summary>
    public static IServiceCollection AddFlowSearch(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(SearchOptions)))
            return services;

        var section = configuration.GetSection(SearchOptions.SectionName);
        services.Configure<SearchOptions>(section);

        // Application читает настройки как обычный синглтон: тянуть туда Microsoft.Extensions.Options
        // ради одной секции незачем, а перечитывать её на лету не нужно — смена модели требует
        // переиндексации, то есть перезапуска с новой конфигурацией.
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<SearchOptions>>().Value);

        var options = section.Get<SearchOptions>() ?? new SearchOptions();

        services.AddScoped<SearchIndexQueue>();
        services.AddScoped<ISearchIndexQueue>(provider => provider.GetRequiredService<SearchIndexQueue>());
        services.AddScoped<ISearchIndexRepository, SearchIndexRepository>();
        services.AddScoped<ISearchQueryRepository, SearchQueryRepository>();

        // Кэш векторов запросов: пагинация по выдаче идёт тем же текстом (см. MemoryCachedQueryEmbeddings).
        services.AddMemoryCache();
        services.AddSingleton<IQueryEmbeddingCache, MemoryCachedQueryEmbeddings>();
        // Извлечение текста из вложений (SearchSourceReader, ветка Attachment).
        // ITextExtractor снаружи один — композитный; форматные регистрируются своими типами,
        // иначе композит попал бы в собственный список и вызвал сам себя.
        services.AddSingleton<PlainTextExtractor>();
        services.AddSingleton<PdfTextExtractor>();
        services.AddSingleton<OpenXmlTextExtractor>();
        services.AddSingleton<ITextExtractor>(provider => new CompositeTextExtractor(
            [
                provider.GetRequiredService<PlainTextExtractor>(),
                provider.GetRequiredService<PdfTextExtractor>(),
                provider.GetRequiredService<OpenXmlTextExtractor>()
            ],
            provider.GetRequiredService<SearchOptions>(),
            provider.GetRequiredService<ILogger<CompositeTextExtractor>>()));

        services.AddScoped<SearchSourceReader>();
        services.AddScoped<SearchIndexingRunner>();

        AddEmbeddingGenerator(services, options);
        AddVisionEmbeddingGenerator(services, options);
        AddReranker(services, options);

        // Воркер не стартует при выключенном поиске: поведение приложения остаётся прежним до байта.
        if (options.Enabled && options.Indexing.Enabled)
            services.AddHostedService<SearchIndexingWorker>();

        return services;
    }

    /// <summary>
    /// Визуальная модель: своё пространство, своя версия, свой сервис (vLLM). Клиент регистрируется
    /// всегда, а включает половину флаг Search:Embeddings:Vision:Enabled вместе с адресом.
    /// </summary>
    private static void AddVisionEmbeddingGenerator(IServiceCollection services, SearchOptions options)
    {
        var vision = options.Embeddings.Vision;

        services.AddHttpClient(HttpVisionEmbeddingGenerator.HttpClientName, client =>
            Configure(client, Normalize(vision.Endpoint), TimeSpan.FromSeconds(Math.Max(1, vision.TimeoutSeconds)), vision.ApiKey));

        services.TryAddSingleton<IVisionEmbeddingGenerator, HttpVisionEmbeddingGenerator>();
    }

    /// <summary>
    /// Вторая ступень выдачи. Клиент регистрируется всегда — включает её флаг Search:Rerank:Enabled
    /// и кнопка «Точнее» в запросе, а не наличие сервиса: без адреса ступень просто недоступна.
    /// </summary>
    private static void AddReranker(IServiceCollection services, SearchOptions options)
    {
        var rerank = options.Rerank;

        services.AddHttpClient(HttpReranker.HttpClientName, client =>
            Configure(client, Normalize(rerank.Endpoint), TimeSpan.FromSeconds(Math.Max(1, rerank.TimeoutSeconds)), rerank.ApiKey));

        // TryAdd: тесты подменяют ступень фейком и модель не тянут.
        services.TryAddSingleton<IReranker, HttpReranker>();
    }

    private static void AddEmbeddingGenerator(IServiceCollection services, SearchOptions options)
    {
        var embeddings = options.Embeddings;

        if (embeddings.Provider == EmbeddingProvider.Onnx)
            throw new NotSupportedException(
                "Search:Embeddings:Provider=Onnx пока не реализован: эмбеддер крутится сайдкаром " +
                "llama-server (Provider=Http). Точка выбора провайдера — AddFlowSearch.");

        var queryEndpoint = Normalize(embeddings.QueryEndpoint);
        var indexingEndpoint = string.IsNullOrWhiteSpace(embeddings.IndexingEndpoint)
            ? queryEndpoint
            : Normalize(embeddings.IndexingEndpoint);

        // Таймауты разные намеренно: запрос пользователя ждать нельзя, прогон индекса — можно.
        services.AddHttpClient(HttpEmbeddingGenerator.QueryClientName, client =>
            Configure(client, queryEndpoint, TimeSpan.FromSeconds(Math.Max(1, embeddings.TimeoutSeconds)), embeddings.ApiKey));

        services.AddHttpClient(HttpEmbeddingGenerator.IndexingClientName, client =>
            Configure(client, indexingEndpoint, TimeSpan.FromSeconds(60), embeddings.ApiKey));

        // TryAdd: интеграционные тесты подменяют эмбеддер фейком и модель не тянут.
        services.TryAddSingleton<IEmbeddingGenerator, HttpEmbeddingGenerator>();
    }

    private static void Configure(HttpClient client, string endpoint, TimeSpan timeout, string? apiKey)
    {
        if (endpoint.Length > 0)
            client.BaseAddress = new Uri(endpoint);

        client.Timeout = timeout;

        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>BaseAddress обязан заканчиваться слэшем, иначе относительный "embeddings" съест последний сегмент пути.</summary>
    private static string Normalize(string endpoint) =>
        string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.TrimEnd('/') + "/";
}
