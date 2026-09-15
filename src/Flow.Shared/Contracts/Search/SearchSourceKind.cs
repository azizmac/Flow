namespace Flow.Shared.Contracts.Search;

/// <summary>
/// Зеркало Flow.Application.Abstractions.SearchSourceType для контрактов: Flow.Shared намеренно
/// не ссылается на другие проекты. Числовые значения совпадают — маппинг в Flow.Api тривиален.
/// </summary>
public enum SearchSourceKind
{
    Task = 1,
    Comment = 2,
    Board = 3,
    User = 4,
    Attachment = 5
}
