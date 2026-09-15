namespace Flow.Shared.Contracts.Search;

/// <summary>
/// Что проиндексировано в чанке. Значения пишутся в БД (SearchChunks.SourceType, SearchIndexQueue.SourceType)
/// и уходят на клиент — менять нельзя, только дописывать.
/// </summary>
public enum SearchSourceType
{
    Task = 1,
    Comment = 2,
    Board = 3,
    User = 4,

    /// <summary>Заведён заранее (ТЗ вложений, этапы 7–8); сейчас не индексируется.</summary>
    Attachment = 5
}
