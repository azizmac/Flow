using Flow.Shared.Contracts.Search;

namespace Flow.Application.Abstractions;

/// <summary>
/// Чтение поискового индекса. Реализация в Flow.Infrastructure — один SQL с двумя CTE (вектор и
/// полнотекст), слиянием RRF и свёрткой чанков в источники. Абстракция здесь не ради тестов, а ради
/// той самой развилки из ТЗ: если pgvector перестанет тянуть объём, меняется только реализация.
/// </summary>
public interface ISearchQueryRepository
{
    Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken);
}

/// <param name="Query">Нормализованная строка запроса — уходит в websearch_to_tsquery и в подсветку.</param>
/// <param name="QueryEmbedding">Вектор запроса; null — векторная половина не выполняется (режим Text или деградация).</param>
/// <param name="UseText">Выполнять ли полнотекстовую половину (в режиме Semantic — нет).</param>
/// <param name="Types">Типы источников; пусто — все.</param>
/// <param name="IncludeArchived">Включать ли задачи в финальном статусе.</param>
public sealed record SearchCriteria(
    string Query,
    float[]? QueryEmbedding,
    bool UseText,
    IReadOnlyCollection<SearchSourceType> Types,
    Guid? BoardId,
    bool IncludeArchived,
    int VectorTopN,
    int TextTopN,
    int RrfK,
    int Limit,
    int Offset);

public sealed record SearchPage(IReadOnlyList<SearchHit> Items, int Total);

/// <param name="ParentId">Задача комментария: сам комментарий открыть негде, открывают его задачу.</param>
public sealed record SearchHit(
    SearchSourceType SourceType,
    Guid SourceId,
    Guid? BoardId,
    string Title,
    string Snippet,
    double Score,
    string? TaskCode,
    DateTime UpdatedAt,
    Guid? ParentId);
