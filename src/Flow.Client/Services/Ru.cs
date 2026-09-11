using System.Text.RegularExpressions;

namespace Flow.Client.Services;

/// <summary>
/// Русская локализация без ICU: склонение числительных и даты «14 авг 2026».
/// InvariantGlobalization включён в csproj, поэтому CultureInfo("ru-RU") недоступна — форматируем вручную.
/// </summary>
public static partial class Ru
{
    private static readonly string[] MonthsShort =
        ["янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}$")]
    public static partial Regex BoardKeyPattern();

    public const string BoardKeyRule = "Заглавные латинские буквы и цифры, 2–10 символов, с буквы";

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,30}[a-z0-9]$")]
    public static partial Regex UsernamePattern();

    public const string UsernameRule = "Строчные латинские буквы, цифры и . _ -, 2–32 символа, не начинается и не заканчивается разделителем";

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    public static partial Regex PhonePattern();

    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
    };

    /// <summary>Подсказка username из имени и фамилии: «Илья Моторин» → «ilya.motorin». Пустая строка, если нечего предложить.</summary>
    public static string SuggestUsername(string firstName, string lastName)
    {
        var parts = new[] { firstName, lastName }
            .Select(Slug)
            .Where(p => p.Length > 0)
            .ToArray();
        var s = string.Join('.', parts);
        if (s.Length > 32) s = s[..32].TrimEnd('.', '_', '-');
        return UsernamePattern().IsMatch(s) ? s : "";
    }

    private static string Slug(string value)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (Translit.TryGetValue(ch, out var t)) sb.Append(t);
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (ch is ' ' or '-' or '_' or '.') sb.Append('-');
        }
        return sb.ToString().Trim('-', '.', '_');
    }

    public static string People(int n) => $"{n} {Plural(n, "человек", "человека", "человек")}";

    /// <summary>Склонение: Plural(n, "задача", "задачи", "задач").</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        var m = Math.Abs(n) % 10;
        var h = Math.Abs(n) % 100;
        if (m == 1 && h != 11) return one;
        if (m is >= 2 and <= 4 && (h < 12 || h > 14)) return few;
        return many;
    }

    public static string Tasks(int n) => $"{n} {Plural(n, "задача", "задачи", "задач")}";

    public static string Projects(int n) => $"{n} {Plural(n, "проект", "проекта", "проектов")}";

    /// <summary>«6 сен» — для строк списка (год добавляется, если отличается от текущего).</summary>
    public static string DateShort(DateTime utc)
    {
        var d = ToLocal(utc);
        var s = $"{d.Day} {MonthsShort[d.Month - 1]}";
        return d.Year == DateTime.Now.Year ? s : $"{s} {d.Year}";
    }

    /// <summary>«14 авг 2026».</summary>
    public static string Date(DateTime utc)
    {
        var d = ToLocal(utc);
        return $"{d.Day} {MonthsShort[d.Month - 1]} {d.Year}";
    }

    /// <summary>«15 сен» для срока (DateOnly, без часового пояса); год — если не текущий.</summary>
    public static string DateOnlyShort(DateOnly d)
    {
        var s = $"{d.Day} {MonthsShort[d.Month - 1]}";
        return d.Year == DateTime.Now.Year ? s : $"{s} {d.Year}";
    }

    /// <summary>Разбор yyyy-MM-dd из журнала активности; null — «без срока» или мусор.</summary>
    public static DateOnly? ParseDateOnly(string? iso) =>
        iso is not null && DateOnly.TryParseExact(iso, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    /// <summary>«только что», «5 мин назад», «2 ч назад», «вчера», дальше — дата.</summary>
    public static string Ago(DateTime utc)
    {
        var d = ToLocal(utc);
        var span = DateTime.Now - d;
        if (span < TimeSpan.FromMinutes(1)) return "только что";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} мин назад";
        if (span < TimeSpan.FromHours(24) && d.Date == DateTime.Today) return $"{(int)span.TotalHours} ч назад";
        if (d.Date == DateTime.Today.AddDays(-1)) return $"вчера в {d.Hour:00}:{d.Minute:00}";
        return DateShort(utc);
    }

    public static string Comments(int n) => $"{n} {Plural(n, "комментарий", "комментария", "комментариев")}";

    /// <summary>«6 сен 2026, 14:20».</summary>
    public static string DateTimeFull(DateTime utc)
    {
        var d = ToLocal(utc);
        return $"{d.Day} {MonthsShort[d.Month - 1]} {d.Year}, {d.Hour:00}:{d.Minute:00}";
    }

    [GeneratedRegex(@"(\*{1,3}|_{1,3}|~~|`+|^#{1,6}\s+|^>\s?|^[-*+]\s+|^\d+\.\s+)", RegexOptions.Multiline)]
    private static partial Regex MarkdownSyntax();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    /// <summary>Первая строка описания без markdown-разметки — для превью в списке задач.</summary>
    public static string PlainFirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var trimmed = text.Trim();
        var nl = trimmed.IndexOfAny(['\r', '\n']);
        var first = nl >= 0 ? trimmed[..nl] : trimmed;
        first = MarkdownLink().Replace(first, "$1");
        return MarkdownSyntax().Replace(first, "").Trim();
    }

    private static DateTime ToLocal(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value.ToLocalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();
}
