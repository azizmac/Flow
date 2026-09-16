using System.Text.RegularExpressions;

namespace Flow.Application.Features.Search;

/// <summary>
/// Что удалось понять в строке запроса: структурированные фильтры и остаток свободного текста.
/// </summary>
/// <param name="Text">То, что осталось после вырезания фильтров — уходит в гибридный поиск.</param>
/// <param name="TaskCode">Код задачи (PROJ-142): по нему открывают задачу напрямую.</param>
/// <param name="AssigneeUsername">Исполнитель из <c>@username</c>.</param>
/// <param name="Mine">Слово «мои» — исполнитель равен текущему пользователю.</param>
/// <param name="Overdue">Слово «просроченные» — срок в прошлом и задача не закрыта.</param>
/// <param name="BoardKey">Значение <c>проект:KEY</c> — ключ или начало названия проекта.</param>
/// <param name="StatusName">Значение <c>статус:…</c>; сопоставляется с названиями статусов уже в хендлере.</param>
/// <param name="Period">Значение «за неделю» и подобных — окно по дате источника.</param>
public sealed record SearchIntent(
    string Text,
    string? TaskCode = null,
    string? AssigneeUsername = null,
    bool Mine = false,
    bool Overdue = false,
    string? BoardKey = null,
    string? StatusName = null,
    TimeSpan? Period = null)
{
    /// <summary>Есть ли хоть один фильтр — от этого зависит, имеет ли смысл пустой текст запроса.</summary>
    public bool HasFilters =>
        TaskCode is not null || AssigneeUsername is not null || Mine || Overdue
        || BoardKey is not null || StatusName is not null || Period is not null;
}

/// <summary>
/// Разбирает строку поиска на фильтры и свободный текст. Детерминированно, на регулярках, без модели:
/// «@ivanov просроченные» — это не смысл, а фильтр, и платить за инференс с ним незачем.
/// Нераспознанное остаётся текстом и уходит в гибрид как есть.
/// </summary>
public static partial class QueryIntentParser
{
    /// <summary>Код задачи: ключ проекта (^[A-Z][A-Z0-9]{1,9}$ в домене) и номер.</summary>
    [GeneratedRegex(@"(?<![\w-])(?<key>[A-Za-z][A-Za-z0-9]{1,9})-(?<number>\d+)(?![\w-])")]
    private static partial Regex TaskCode();

    /// <summary>@username по правилам домена; не после буквы, цифры и слэша — почта и пути не упоминания.</summary>
    [GeneratedRegex(@"(?<![\w/@])@(?<name>[a-z0-9][a-z0-9._-]{0,30}[a-z0-9])(?![\w.-])", RegexOptions.IgnoreCase)]
    private static partial Regex Assignee();

    [GeneratedRegex(@"(?<![\w])проект:(?<value>""[^""]+""|[^\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BoardFilter();

    /// <summary>
    /// Статус может быть из двух слов («в работе»), поэтому забираем до кавычек или до двух слов.
    /// Для длинных названий есть кавычки: <c>статус:"готово к релизу"</c>.
    /// </summary>
    [GeneratedRegex(@"(?<![\w])статус:(?<value>""[^""]+""|[^\s@]+(?:\s+[^\s@:]+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex StatusFilter();

    [GeneratedRegex(@"(?<![\w])мои(?![\w])", RegexOptions.IgnoreCase)]
    private static partial Regex Mine();

    [GeneratedRegex(@"(?<![\w])просрочен\w*(?![\w])", RegexOptions.IgnoreCase)]
    private static partial Regex Overdue();

    /// <summary>«за день», «за неделю», «за месяц», «за год» — окно по дате источника.</summary>
    [GeneratedRegex(@"(?<![\w])за\s+(?<period>день|сутки|неделю|месяц|год)(?![\w])", RegexOptions.IgnoreCase)]
    private static partial Regex Period();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static SearchIntent Parse(string? query)
    {
        var text = query ?? string.Empty;

        // Порядок важен: сначала фильтры с двоеточием, иначе «проект:DBACK» потеряет часть значения
        // при вырезании кода задачи или @имени.
        text = Take(text, BoardFilter(), "value", out var boardKey);
        text = Take(text, StatusFilter(), "value", out var statusName);
        text = Take(text, TaskCode(), null, out var taskCode);
        text = Take(text, Assignee(), "name", out var assignee);
        text = Take(text, Period(), "period", out var period);
        text = TakeFlag(text, Mine(), out var mine);
        text = TakeFlag(text, Overdue(), out var overdue);

        return new SearchIntent(
            Whitespace().Replace(text, " ").Trim(),
            taskCode?.ToUpperInvariant(),
            assignee?.ToLowerInvariant(),
            mine,
            overdue,
            Unquote(boardKey),
            Unquote(statusName),
            ToPeriod(period));
    }

    /// <summary>Вырезает первое совпадение и отдаёт его значение (или всё совпадение, если группа не задана).</summary>
    private static string Take(string text, Regex pattern, string? group, out string? value)
    {
        var match = pattern.Match(text);
        if (!match.Success)
        {
            value = null;
            return text;
        }

        value = group is null ? match.Value : match.Groups[group].Value;
        return text.Remove(match.Index, match.Length);
    }

    private static string TakeFlag(string text, Regex pattern, out bool found)
    {
        var match = pattern.Match(text);
        found = match.Success;
        return found ? text.Remove(match.Index, match.Length) : text;
    }

    private static string? Unquote(string? value) =>
        value is null ? null : value.Trim().Trim('"').Trim() is { Length: > 0 } trimmed ? trimmed : null;

    private static TimeSpan? ToPeriod(string? period) => period?.ToLowerInvariant() switch
    {
        "день" or "сутки" => TimeSpan.FromDays(1),
        "неделю" => TimeSpan.FromDays(7),
        "месяц" => TimeSpan.FromDays(30),
        "год" => TimeSpan.FromDays(365),
        _ => null
    };
}
