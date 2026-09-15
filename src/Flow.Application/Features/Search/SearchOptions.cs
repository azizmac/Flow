namespace Flow.Application.Features.Search;

/// <summary>
/// Секция "Search" (Flow.Api appsettings.json, в compose переопределяется переменными Search__*).
/// Биндится в Flow.Infrastructure (AddFlowSearch) и регистрируется синглтоном, чтобы хендлеры
/// Application читали её без зависимости на Microsoft.Extensions.Options.
/// </summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>Общий выключатель: воркер не стартует, очередь не наполняется, /search/status отвечает enabled: false.</summary>
    public bool Enabled { get; set; }

    public SearchEmbeddingsOptions Embeddings { get; set; } = new();

    public SearchIndexingOptions Indexing { get; set; } = new();
}

public sealed class SearchEmbeddingsOptions
{
    /// <summary>Http — сайдкар llama-server (OpenAI-совместимый /v1/embeddings). Onnx — этап «дальше по потребности».</summary>
    public EmbeddingProvider Provider { get; set; } = EmbeddingProvider.Http;

    /// <summary>Эмбеддинг запросов — низкая латентность. Без схемы и без /embeddings на конце: http://embeddings:8081/v1.</summary>
    public string QueryEndpoint { get; set; } = string.Empty;

    /// <summary>Эмбеддинг индексации; пусто — тот же, что для запросов.</summary>
    public string IndexingEndpoint { get; set; } = string.Empty;

    public string Model { get; set; } = "Qwen3-Embedding-0.6B";

    /// <summary>Необязательный Bearer для внешнего провайдера.</summary>
    public string? ApiKey { get; set; }

    /// <summary>MRL-урезание: 512 по умолчанию, 1024 — полная размерность Qwen3-Embedding-0.6B.</summary>
    public int Dimensions { get; set; } = 512;

    /// <summary>Инструкция запроса (документы идут без неё). Входит в ModelVersion: сменили — переиндексируйте.</summary>
    public string QueryInstruction { get; set; } =
        "Given a search query, retrieve relevant tasks, comments and documents";

    public int BatchSize { get; set; } = 16;

    /// <summary>Таймаут запросов поиска; у индексации свой, 60 с.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}

public enum EmbeddingProvider
{
    Http = 0,
    Onnx = 1
}

public sealed class SearchIndexingOptions
{
    /// <summary>Фоновый воркер индексации внутри Flow.Api. Выключается, когда индексацию выносят в отдельный хост.</summary>
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 32;

    /// <summary>После стольких неудач запись остаётся в очереди и попадает в queueStuck.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Целевая длина чанка в токенах (считаются приближённо, см. TextChunker).</summary>
    public int ChunkTokens { get; set; } = 512;

    public int ChunkOverlap { get; set; } = 64;
}
