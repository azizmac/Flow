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

    /// <summary>Вложение задачи: текст файла и, за флагом, кадр (ТЗ вложений, этапы 7–8).</summary>
    Attachment = 5,

    /// <summary>
    /// PR или коммит задачи с Git-хостинга (docs/TZ_scm_integration.md, этап 5E): заголовок PR или первая строка
    /// сообщения коммита. Источник — строка ScmLinks; ветки не индексируются (их имя — код и название задачи).
    /// </summary>
    Development = 6
}
