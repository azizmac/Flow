using System.Text;

namespace Flow.Application.Features.Tasks.Fql;

public enum FqlTokenKind { Word, String, Operator, LParen, RParen, Comma, End }

/// <summary>
/// Токен FQL. Word — всё, что не строка в кавычках и не пунктуация: ключевые слова, имена полей, значения
/// (PROJ-1, -7d, 2026-09-01, @ivan, «Сделана»). Что из них ключевое слово — решает парсер по контексту,
/// поэтому статус «Order» или проект «IN» остаются возможны в кавычках.
/// </summary>
public sealed record FqlToken(FqlTokenKind Kind, string Text, int Position, int Length)
{
    public bool Is(string keyword) => Kind == FqlTokenKind.Word && string.Equals(Text, keyword, StringComparison.OrdinalIgnoreCase);
}

public static class FqlTokenizer
{
    public static IReadOnlyList<FqlToken> Tokenize(string query)
    {
        var tokens = new List<FqlToken>();
        var i = 0;
        while (i < query.Length)
        {
            var c = query[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '(':
                    tokens.Add(new FqlToken(FqlTokenKind.LParen, "(", i, 1));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new FqlToken(FqlTokenKind.RParen, ")", i, 1));
                    i++;
                    continue;
                case ',':
                    tokens.Add(new FqlToken(FqlTokenKind.Comma, ",", i, 1));
                    i++;
                    continue;
                case '"' or '\'':
                    tokens.Add(ReadString(query, ref i));
                    continue;
                case '=' or '~':
                    tokens.Add(new FqlToken(FqlTokenKind.Operator, c.ToString(), i, 1));
                    i++;
                    continue;
                case '!' or '<' or '>':
                {
                    var two = i + 1 < query.Length && query[i + 1] == '=';
                    if (c == '!' && !two)
                        throw new FqlException("Ожидался оператор «!=»", i, 1);

                    tokens.Add(new FqlToken(FqlTokenKind.Operator, two ? c + "=" : c.ToString(), i, two ? 2 : 1));
                    i += two ? 2 : 1;
                    continue;
                }
            }

            var start = i;
            while (i < query.Length && IsWordChar(query[i]))
                i++;

            if (i == start)
                throw new FqlException($"Непонятный символ «{c}»", start, 1);

            tokens.Add(new FqlToken(FqlTokenKind.Word, query[start..i], start, i - start));
        }

        tokens.Add(new FqlToken(FqlTokenKind.End, "", query.Length, 0));
        return tokens;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' or '+' or '.' or '@' or ':' or '/';

    private static FqlToken ReadString(string query, ref int i)
    {
        var quote = query[i];
        var start = i++;
        var text = new StringBuilder();
        while (i < query.Length && query[i] != quote)
        {
            if (query[i] == '\\' && i + 1 < query.Length)
                i++;
            text.Append(query[i++]);
        }

        if (i >= query.Length)
            throw new FqlException("Строка не закрыта кавычкой", start, query.Length - start);

        i++;
        return new FqlToken(FqlTokenKind.String, text.ToString(), start, i - start);
    }
}
