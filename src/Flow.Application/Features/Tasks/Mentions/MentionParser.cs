using System.Text.RegularExpressions;

namespace Flow.Application.Features.Tasks.Mentions;

/// <summary>
/// Находит <c>@username</c> в Markdown-тексте комментария. Username — по правилу User (a-z, 0-9, точка, дефис,
/// подчёркивание, 2..32 символа), регистр не важен, результат в lower. Не считается упоминанием: часть e-mail
/// или пути (перед @ буква, цифра или /), а также всё внутри code-span (`...`) и fenced-блоков (```...```).
/// </summary>
public static partial class MentionParser
{
    [GeneratedRegex(@"(?<![\w/])@([a-z0-9][a-z0-9._-]{0,30}[a-z0-9])(?![\w-])", RegexOptions.IgnoreCase)]
    private static partial Regex MentionPattern();

    [GeneratedRegex(@"```[\s\S]*?```|`[^`\n]*`")]
    private static partial Regex CodePattern();

    public static IReadOnlySet<string> Parse(string body)
    {
        if (string.IsNullOrEmpty(body))
            return new HashSet<string>();

        // Код вырезаем, а не пропускаем внутри регулярки: так проще и не ломает границы слов вокруг.
        var withoutCode = CodePattern().Replace(body, " ");

        return MentionPattern().Matches(withoutCode)
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .ToHashSet();
    }
}
