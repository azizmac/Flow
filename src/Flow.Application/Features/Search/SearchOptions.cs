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

    public SearchQueryOptions Query { get; set; } = new();

    public SearchRerankOptions Rerank { get; set; } = new();

    /// <summary>
    /// Визуальная половина. Зависит от Embeddings:Enabled намеренно: это вторая нейросеть, и выключатель
    /// «без моделей» должен гасить обе, иначе установка без GPU продолжала бы ходить в vLLM за кадрами.
    /// </summary>
    public bool VisionEnabled => Embeddings.Enabled && Embeddings.Vision.Enabled;

    /// <summary>Вторая ступень выдачи — тоже модель, поэтому тоже гаснет вместе с Embeddings:Enabled.</summary>
    public bool RerankEnabled => Embeddings.Enabled && Rerank.Enabled;
}

/// <summary>
/// Вторая ступень выдачи (docs/TZ_search_vector.md, «Реранкер»). В ТЗ флаг назван
/// Search:Query:RerankEnabled; здесь он лежит рядом с адресом и моделью — как у эмбеддера,
/// иначе настройки одной ступени оказались бы в двух секциях.
/// </summary>
public sealed class SearchRerankOptions
{
    /// <summary>
    /// Выключено по умолчанию: реранкер меняет ощущение поиска с «мгновенно» на «секунда»,
    /// и это решение продукта, а не значение по умолчанию.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Адрес модели без /rerank на конце: http://reranker:8082/v1.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string Model { get; set; } = "bge-reranker-v2-m3";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Сколько кандидатов уходит второй ступени. В ТЗ стоит 25, но замер на тестовой базе показал
    /// обратное: окно 25 роняет recall@10 с 0.948 до 0.933 и MRR с 0.791 до 0.760, окно 15 — до 0.741,
    /// а окно 10 (ровно страница) поднимает MRR до 0.807, не трогая recall. На корпусе в шесть сотен
    /// чанков кандидаты с 11-го места хуже тех, кого они вытесняют, и модель тут помогает порядку,
    /// а не полноте. Значение имеет смысл поднимать вместе с объёмом базы и перемеривать golden-set.
    /// </summary>
    public int TopN { get; set; } = 10;

    /// <summary>
    /// Сколько символов документа уходит в модель. Cross-encoder платит за каждый токен пары,
    /// а решают обычно первые абзацы: ограничение держит запрос в районе секунды.
    /// </summary>
    public int MaxDocumentChars { get; set; } = 1200;

    /// <summary>Больше, чем у эмбеддера: прогон пар дороже, а пользователь уже нажал «Точнее».</summary>
    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class SearchEmbeddingsOptions
{
    /// <summary>
    /// Главный выключатель «умного поиска». false — ни одна модель не вызывается: запрос ищется
    /// только по словам (полнотекст), картинки и вторая ступень выключены вместе с векторами
    /// (см. SearchOptions.VisionEnabled и RerankEnabled), а индексация продолжает работать и пишет
    /// чанки без вектора — текст остаётся находимым, и установке без GPU не нужен ни один сайдкар.
    ///
    /// Векторы этих чанков дозаполняются, когда модель возвращается: воркер на старте ставит такие
    /// источники в очередь сам (см. SearchIndexingWorker), вручную это же делает POST /search/reindex.
    /// </summary>
    public bool Enabled { get; set; } = true;

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

    public SearchVisionOptions Vision { get; set; } = new();
}

/// <summary>
/// Визуальные эмбеддинги картинок (docs/TZ_search_vector.md, «Мультимодальность»). Своя модель,
/// своё векторное пространство и своя ModelVersion: смешивать их с текстовыми нельзя, поэтому
/// каждая половина запроса ищет только среди своих чанков.
/// </summary>
public sealed class SearchVisionOptions
{
    /// <summary>
    /// Выключено по умолчанию: визуальная модель — это ещё полтора гигабайта весов и заметно более
    /// дорогой прогон, а без неё картинки индексируются именем файла, то есть поиск не ломается.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Адрес без /embeddings на конце. Это ТОТ ЖЕ адрес, что у текстовой половины
    /// (http://flow-ai.flow-ai.svc.cluster.local:8081/v1): визуальная модель живёт третьей секцией
    /// в пресете одного llama-server, а не отдельным сервисом, и роутер различает модели по полю
    /// "model" в теле запроса.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Имя секции в docker/ai/models.ini — по нему роутер выбирает процесс модели.</summary>
    public string Model { get; set; } = "Qwen3-VL-Embedding-2B";

    /// <summary>
    /// Имя файла весов. Приложению оно нужно ровно для одного: попасть в ModelVersion. Имя модели
    /// в роутере не меняется при смене квантизации, а вектор меняется — без файла в версии старые
    /// векторы остались бы в базе рядом с новыми и молча испортили бы выдачу. Совпадение с пресетом
    /// проверяет scripts/check-ai-preset.py.
    /// </summary>
    public string ModelFile { get; set; } = "Qwen3-VL-Embedding-2B.Q4_K_M.gguf";

    public string? ApiKey { get; set; }

    /// <summary>MRL-урезание с нативных 2048. Должно совпадать с размерностью текстовой половины — колонка одна.</summary>
    public int Dimensions { get; set; } = 512;

    /// <summary>
    /// Потолок картинки, которая уходит в модель. В ТЗ стоит ограничение по пикселям, но ресайз
    /// потребовал бы графической библиотеки на нашей стороне, а препроцессор модели и так масштабирует
    /// вход сам. Ограничение по размеру файла решает ту же задачу — не гнать в модель многомегабайтный
    /// кадр — и ничего не стоит.
    /// </summary>
    public long MaxBytes { get; set; } = 8L * 1024 * 1024;

    /// <summary>
    /// Сколько первых страниц скана уходит в визуальный индекс. Каждая — отдельный прогон модели
    /// и отдельная строка, поэтому не весь документ: у договора на тридцать страниц ищут по первым.
    /// </summary>
    public int MaxPdfPages { get; set; } = 3;

    /// <summary>Индексация ждёт дольше поиска: прогон картинки дороже прогона строки.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

public enum EmbeddingProvider
{
    Http = 0,
    Onnx = 1
}

/// <summary>Параметры выдачи GET /search.</summary>
public sealed class SearchQueryOptions
{
    /// <summary>Сколько чанков берёт векторная половина до слияния.</summary>
    public int VectorTopN { get; set; } = 50;

    /// <summary>Сколько чанков берёт полнотекстовая половина до слияния.</summary>
    public int TextTopN { get; set; } = 50;

    /// <summary>
    /// k в RRF: score = сумма 1 / (k + позиция). Чем больше k, тем меньше веса у самых первых мест
    /// и тем сильнее «голос» второй половины. 60 — значение из исходной статьи и дефолт де-факто.
    /// </summary>
    public int RrfK { get; set; } = 60;

    /// <summary>Потолок limit в запросе: выше него выдача не имеет смысла, а стоимость растёт.</summary>
    public int MaxLimit { get; set; } = 50;

    /// <summary>ef_search HNSW в рантайме: больше — полнее обход графа и дороже запрос.</summary>
    public int HnswEfSearch { get; set; } = 100;
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

    /// <summary>
    /// Потолок текста, извлечённого из вложения. Договор на 300 страниц дал бы сотни чанков и
    /// столько же обращений к модели; найтись он должен и по началу.
    /// </summary>
    public int MaxDocumentChars { get; set; } = 200_000;
}
