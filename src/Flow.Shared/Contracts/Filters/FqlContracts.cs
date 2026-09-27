namespace Flow.Shared.Contracts.Filters;

/// <summary>Ошибка FQL: место в строке, чтобы подчеркнуть его (400 у /tasks?fql= и /filters).</summary>
public sealed record FqlErrorResponse(string Message, int Position, int Length);

/// <summary>
/// Подсказка у курсора (GET /tasks/query/suggest): Insert заменяет в строке кусок [ReplaceFrom, ReplaceFrom + ReplaceLength)
/// ответа — недописанное слово под курсором. Kind — field | operator | value | keyword, для иконки.
/// </summary>
public sealed record FqlSuggestion(string Insert, string Label, string? Hint, string Kind);

public sealed record FqlSuggestResponse(IReadOnlyList<FqlSuggestion> Items, int ReplaceFrom, int ReplaceLength);
