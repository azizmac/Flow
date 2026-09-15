namespace Flow.Shared.Contracts.Search;

/// <summary>Сколько чанков лежит в индексе по каждому типу источника.</summary>
public sealed record SearchChunkCountsResponse(int Task, int Comment, int Board, int User);
