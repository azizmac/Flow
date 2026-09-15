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
public sealed record SearchResponse(
    IReadOnlyList<SearchResultItem> Items,
    int Total,
    bool Degraded,
    long TookMs,
    SearchMode Mode);

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
