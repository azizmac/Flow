namespace Flow.Shared.Contracts.Search;

/// <summary>
/// Ответ GET /search. Результаты — источники, а не чанки: у источника берётся его лучший чанк,
/// он же идёт в подсветку.
/// </summary>
/// <param name="Total">Сколько источников нашлось в окне поиска (оно ограничено VectorTopN/TextTopN),
/// а не во всей базе: гибридная выдача точного «всего» не знает и не обещает.</param>
/// <param name="Degraded">Векторную половину выполнить не удалось (модель недоступна или не уложилась
/// в таймаут) — выдача построена только по полнотексту.</param>
/// <param name="Mode">Режим, в котором запрос реально выполнен; при деградации — Text.</param>
/// <param name="Reranked">Выдача переупорядочена второй ступенью. false при запрошенном rerank означает,
/// что ступень выключена, недоступна или не нужна для этой страницы — порядок остался гибридным.</param>
/// <param name="Intent">Что распознано в строке запроса: фильтры и прямое попадание по коду задачи.</param>
public sealed record SearchResponse(
    IReadOnlyList<SearchResultItem> Items,
    int Total,
    bool Degraded,
    long TookMs,
    SearchMode Mode,
    SearchIntentResponse Intent,
    bool Reranked = false);

/// <summary>
/// Разбор строки запроса: «@ivanov просроченные проект:DBACK» — это фильтры, а не смысл, и стоят
/// они ноль. Клиент показывает их чипами, иначе человек не поймёт, почему выдача сузилась.
/// </summary>
/// <param name="Text">Остаток строки, ушедший в поиск; пусто — были только фильтры.</param>
/// <param name="Filters">Человекочитаемые подписи распознанного — для чипов.</param>
/// <param name="TaskId">Задача, найденная по коду в строке (PROJ-142): её открывают напрямую.</param>
public sealed record SearchIntentResponse(string Text, IReadOnlyList<string> Filters, Guid? TaskId);

/// <param name="Title">Название источника: задачи, задачи-владельца комментария, проекта или человека.</param>
/// <param name="Snippet">Фрагмент найденного чанка; совпадения обёрнуты в &lt;mark&gt;.</param>
/// <param name="Score">Оценка RRF — сравнима только внутри одной выдачи.</param>
/// <param name="TaskCode">Код задачи (PROJ-142) у задач и комментариев; у проектов и людей null.</param>
/// <param name="ParentId">Владелец источника: у комментария — его задача. Нужен, чтобы результат
/// было куда открыть: сам комментарий отдельной страницы не имеет. У остальных типов null.</param>
public sealed record SearchResultItem(
    SearchSourceType SourceType,
    Guid SourceId,
    Guid? BoardId,
    string Title,
    string Snippet,
    double Score,
    string? TaskCode,
    DateTime UpdatedAt,
    Guid? ParentId);
