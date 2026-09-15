using Flow.Shared.Contracts.Search;

namespace Flow.Application.Abstractions;

/// <summary>Что сделать с источником в индексе.</summary>
public enum SearchIndexOperation
{
    Upsert = 1,
    Delete = 2
}

/// <summary>
/// Outbox изменений для поискового индекса. Ставится из хендлеров рядом с записью в журнал
/// <c>TaskActivity</c>: доменных событий в проекте нет намеренно.
/// </summary>
public interface ISearchIndexQueue
{
    /// <summary>
    /// Ставит источник в очередь. Коммитится той же <see cref="IUnitOfWork.SaveChangesAsync"/>,
    /// что и изменение сущности: потерять правку или проиндексировать откатившуюся нельзя.
    /// Повтор по (SourceType, SourceId, Operation) не плодит строк — обновляет момент постановки
    /// и берёт минимальный приоритет.
    /// </summary>
    /// <param name="priority">0 — живые правки, 1 — массовая переиндексация. Меньше — раньше.</param>
    void Enqueue(SearchSourceType sourceType, Guid sourceId, Guid? boardId, SearchIndexOperation operation, int priority = 0);
}
