namespace Flow.Shared.Contracts.Search;

/// <summary>
/// Тело POST /search/reindex. Оба поля необязательны: без них переиндексируется всё.
/// </summary>
/// <param name="Types">Какие типы источников поставить в очередь; null или пусто — все.</param>
/// <param name="BoardId">Сузить до одного проекта. Для людей игнорируется — они не принадлежат проекту.</param>
public sealed record ReindexRequest(IReadOnlyList<SearchSourceKind>? Types = null, Guid? BoardId = null);
