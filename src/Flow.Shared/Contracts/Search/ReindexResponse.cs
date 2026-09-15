namespace Flow.Shared.Contracts.Search;

/// <summary>Ответ 202 на POST /search/reindex: сколько источников поставлено в очередь.</summary>
public sealed record ReindexResponse(int Queued);
