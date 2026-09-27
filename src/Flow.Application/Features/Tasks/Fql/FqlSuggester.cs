namespace Flow.Application.Features.Tasks.Fql;

public enum FqlSlot { Field, Operator, Value, Connector, OrderField, OrderNext }

/// <summary>
/// Что ожидается у курсора: поле, оператор, значение (и для какого поля), связка AND/OR/ORDER BY или поле сортировки.
/// Partial — недописанное слово под курсором, [ReplaceFrom, ReplaceFrom + ReplaceLength) — что заменит подсказка.
/// </summary>
public sealed record FqlCursor(FqlSlot Slot, string? Field, string Partial, int ReplaceFrom, int ReplaceLength);

/// <summary>
/// Разбор строки до курсора для подсказок (GET /tasks/query/suggest). Не парсер: строка под курсором почти всегда
/// недописана, поэтому достаточно последних токенов. Ошибки токенизатора (незакрытая кавычка) — «ждём значение».
/// </summary>
public static class FqlSuggester
{
    public static FqlCursor Analyze(string query, int position)
    {
        position = Math.Clamp(position, 0, query.Length);
        var before = query[..position];

        IReadOnlyList<FqlToken> tokens;
        try
        {
            tokens = FqlTokenizer.Tokenize(before).Where(t => t.Kind != FqlTokenKind.End).ToList();
        }
        catch (FqlException error)
        {
            // Незакрытая строка: человек печатает значение в кавычках.
            return new FqlCursor(FqlSlot.Value, FieldBefore(FqlTokenizer.Tokenize(before[..error.Position]).ToList()), before[(error.Position + 1)..],
                error.Position, position - error.Position);
        }

        // Слово вплотную к курсору — недописанное: его и заменит подсказка.
        var partial = "";
        var from = position;
        if (tokens.Count > 0 && tokens[^1].Kind == FqlTokenKind.Word && tokens[^1].Position + tokens[^1].Length == position)
        {
            partial = tokens[^1].Text;
            from = tokens[^1].Position;
            tokens = tokens.Take(tokens.Count - 1).ToList();
        }

        var cursor = Slot(tokens);
        return new FqlCursor(cursor.Slot, cursor.Field, partial, from, position - from);
    }

    private static (FqlSlot Slot, string? Field) Slot(IReadOnlyList<FqlToken> tokens)
    {
        if (tokens.Count == 0)
            return (FqlSlot.Field, null);

        // После ORDER BY — поля сортировки, после поля — ASC/DESC или запятая.
        var orderAt = LastIndex(tokens, t => t.Is("ORDER"));
        if (orderAt >= 0)
        {
            var last = tokens[^1];
            return last.Is("BY") || last.Kind == FqlTokenKind.Comma ? (FqlSlot.OrderField, null) : (FqlSlot.OrderNext, null);
        }

        var prev = tokens[^1];
        if (prev.Is("AND") || prev.Is("OR") || prev.Is("NOT") && !(tokens.Count >= 2 && IsFieldToken(tokens, tokens.Count - 2)))
            return (FqlSlot.Field, null);

        switch (prev.Kind)
        {
            case FqlTokenKind.LParen:
                // «IN (» — список значений; просто «(» — группа условий.
                return tokens.Count >= 2 && tokens[^2].Is("IN") ? (FqlSlot.Value, FieldBefore(tokens)) : (FqlSlot.Field, null);
            case FqlTokenKind.Comma:
                return (FqlSlot.Value, FieldBefore(tokens));
            case FqlTokenKind.Operator:
                return (FqlSlot.Value, FieldBefore(tokens));
            case FqlTokenKind.RParen:
            case FqlTokenKind.String:
                return (FqlSlot.Connector, null);
        }

        // Слово: поле (ждём оператор), IS/NOT (ждём EMPTY/IN) или значение (ждём связку).
        if (prev.Is("IS") || prev.Is("NOT"))
            return (FqlSlot.Operator, FieldBefore(tokens));
        if (IsFieldToken(tokens, tokens.Count - 1))
            return (FqlSlot.Operator, prev.Text);

        return (FqlSlot.Connector, null);
    }

    /// <summary>Слово — поле, если перед ним начало, связка, NOT или открывающая скобка группы.</summary>
    private static bool IsFieldToken(IReadOnlyList<FqlToken> tokens, int index)
    {
        if (tokens[index].Kind != FqlTokenKind.Word)
            return false;
        if (index == 0)
            return true;

        var before = tokens[index - 1];
        return before.Is("AND") || before.Is("OR") || before.Is("NOT")
               || before.Kind == FqlTokenKind.LParen && !(index >= 2 && tokens[index - 2].Is("IN"));
    }

    /// <summary>Ближайшее поле слева — для значений и операторов.</summary>
    private static string? FieldBefore(IReadOnlyList<FqlToken> tokens)
    {
        for (var i = tokens.Count - 1; i >= 0; i--)
            if (IsFieldToken(tokens, i))
                return tokens[i].Text;
        return null;
    }

    private static int LastIndex(IReadOnlyList<FqlToken> tokens, Func<FqlToken, bool> match)
    {
        for (var i = tokens.Count - 1; i >= 0; i--)
            if (match(tokens[i]))
                return i;
        return -1;
    }

    /// <summary>Операторы, которые принимает поле (тот же набор, что проверяет биндер).</summary>
    public static IReadOnlyList<string> OperatorsFor(string? field) => field?.ToLowerInvariant() switch
    {
        "text" => ["~"],
        "priority" or "points" or "estimate" or "start" or "due" => ["=", "!=", ">", ">=", "<", "<=", "IN ()", "IS EMPTY", "IS NOT EMPTY"],
        "created" or "updated" => ["=", "!=", ">", ">=", "<", "<="],
        "assignee" or "parent" or "linked" or "sprint" or "milestone" => ["=", "!=", "IN ()", "NOT IN ()", "IS EMPTY", "IS NOT EMPTY"],
        _ => ["=", "!=", "IN ()", "NOT IN ()"]
    };

    /// <summary>Значение в кавычках, если в нём пробел или символ, который токенизатор не примет в слове.</summary>
    public static string Quote(string value) =>
        value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '@') ? value : $"\"{value.Replace("\"", "\\\"")}\"";
}
