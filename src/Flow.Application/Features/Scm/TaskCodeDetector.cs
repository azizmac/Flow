using System.Text.RegularExpressions;

namespace Flow.Application.Features.Scm;

/// <summary>
/// Коды задач в тексте ветки, коммита или PR (docs/TZ_scm_integration.md): ключ проекта по правилу Board.Key и номер,
/// на границах слова. «WEB-12a» и «XWEB-12» — не коды; в URL код находится, если стоит отдельным сегментом.
/// В ветках разделителем бывает и «_»/«/»: feature/WEB-12_login — тоже WEB-12.
/// </summary>
public static partial class TaskCodeDetector
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])([A-Z][A-Z0-9]{1,9})-(\d{1,9})(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])([A-Za-z][A-Za-z0-9]{1,9})-(\d{1,9})(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex AnyCasePattern();

    /// <summary>В имени ветки регистр не важен: git checkout -b web-12-login пишут чаще, чем WEB-12.</summary>
    public static IReadOnlyList<string> FindInBranch(string? branch) =>
        string.IsNullOrEmpty(branch)
            ? []
            : AnyCasePattern().Matches(branch).Select(m => $"{m.Groups[1].Value.ToUpperInvariant()}-{int.Parse(m.Groups[2].Value)}").Distinct().ToList();

    public static IReadOnlyList<string> Find(params string?[] texts) =>
        texts.Where(t => !string.IsNullOrEmpty(t))
            .SelectMany(t => CodePattern().Matches(t!).Select(m => $"{m.Groups[1].Value}-{int.Parse(m.Groups[2].Value)}"))
            .Distinct()
            .ToList();
}
