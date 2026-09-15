namespace Flow.Application.Abstractions;

/// <summary>
/// Что именно проиндексировано. Значения зафиксированы: они лежат в столбце "SourceType" таблицы
/// "SearchChunks" целыми числами, менять их задним числом нельзя.
/// <see cref="Attachment"/> заведён заранее (этапы 7–8 ТЗ поиска) и пока не используется.
/// </summary>
public enum SearchSourceType
{
    Task = 1,
    Comment = 2,
    Board = 3,
    User = 4,
    Attachment = 5
}
