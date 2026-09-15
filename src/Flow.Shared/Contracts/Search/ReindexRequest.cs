namespace Flow.Shared.Contracts.Search;

/// <summary>
/// Тело POST /search/reindex (Owner). Оба поля необязательны: без них в очередь уходит всё.
/// </summary>
/// <param name="Types">Типы источников; null или пусто — все четыре.</param>
/// <param name="BoardId">Только один проект (задачи, комментарии и сам проект); null — все.</param>
public sealed record ReindexRequest(IReadOnlyList<SearchSourceType>? Types = null, Guid? BoardId = null);
