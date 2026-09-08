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
