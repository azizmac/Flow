namespace Flow.Application.Abstractions;

/// <summary>
/// Секция "Search" конфигурации. Простой POCO, а не IOptions: Flow.Application не тянет
/// Microsoft.Extensions.Options, а связывает секцию и кладёт готовый экземпляр в контейнер
/// Flow.Infrastructure (AddFlowSearch).
/// </summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>
    /// Главный рубильник. false (дефолт в репозитории) — поиска нет вовсе: очередь не пополняется,
    /// воркер не стартует, /search/status отвечает enabled: false, к эмбеддеру никто не ходит.
    /// </summary>
    public bool Enabled { get; set; }

    public SearchEmbeddingOptions Embeddings { get; set; } = new();

    public SearchIndexingOptions Indexing { get; set; } = new();
}

public sealed class SearchEmbeddingOptions
{
    /// <summary>Где считаются векторы. В этой ветке реализован только Http (сайдкар llama-server).</summary>
    public EmbeddingProvider Provider { get; set; } = EmbeddingProvider.Http;

    /// <summary>Базовый адрес OpenAI-совместимого API для запросов пользователя (низкая задержка).</summary>
    public string QueryEndpoint { get; set; } = string.Empty;

    /// <summary>Тот же API для фоновой индексации. Пусто — тот же, что <see cref="QueryEndpoint"/>.</summary>
    public string IndexingEndpoint { get; set; } = string.Empty;

    public string Model { get; set; } = "Qwen3-Embedding-0.6B";

    /// <summary>Размерность, до которой урезается вектор (MRL). Совпадает с halfvec(N) в схеме.</summary>
    public int Dimensions { get; set; } = 512;

    /// <summary>Instruct-префикс запроса. Входит в ModelVersion: сменили инструкцию — векторы несовместимы.</summary>
    public string QueryInstruction { get; set; } =
        "Given a search query, retrieve relevant tasks, comments, projects and people";

    public int BatchSize { get; set; } = 16;

    /// <summary>Таймаут клиента запросов. Индексация ходит с фиксированными 60 с.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}

public sealed class SearchIndexingOptions
{
    /// <summary>Выключает фонового воркера, не трогая остальной поиск.</summary>
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 32;

    /// <summary>После стольких неудач запись остаётся в очереди и считается застрявшей (queueStuck).</summary>
    public int MaxAttempts { get; set; } = 10;

    public int ChunkTokens { get; set; } = 512;

    public int ChunkOverlap { get; set; } = 64;
}

public enum EmbeddingProvider
{
    /// <summary>Сайдкар llama-server по OpenAI-совместимому POST /v1/embeddings.</summary>
    Http = 0,

    /// <summary>Модель в процессе через ONNX Runtime. Точка выбора заложена, реализации пока нет.</summary>
    Onnx = 1
}
