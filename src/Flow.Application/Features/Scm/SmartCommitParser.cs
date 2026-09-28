using System.Text.RegularExpressions;

namespace Flow.Application.Features.Scm;

public enum SmartCommandKind { Done, Comment, Status }

/// <summary>Команда смарт-коммита: код задачи, что сделать и аргумент (текст комментария, имя статуса).</summary>
public sealed record SmartCommand(string Code, SmartCommandKind Kind, string? Argument);

/// <summary>
/// Смарт-коммиты (docs/TZ_scm_integration.md §5): «WEB-12 #done», «WEB-12 #comment текст», «WEB-12 #status "В работе"».
/// Разбор построчно: команды строки относятся к кодам, стоящим в ней до первой команды («WEB-1 WEB-2 #done» закрывает
/// обе), аргумент — текст до следующей команды или конца строки. Коды после первой команды — часть аргумента, а не
/// новые цели: «#comment см. WEB-3» не комментирует WEB-3. #time (worklog) вне объёма и не распознаётся.
/// </summary>
public static partial class SmartCommitParser
{
    [GeneratedRegex(@"(?<![\w#])#(done|comment|status)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommandPattern();

    public const int MaxCommentLength = 2000;

    public static IReadOnlyList<SmartCommand> Parse(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        var result = new List<SmartCommand>();
        foreach (var line in message.Split('\n'))
        {
            var commands = CommandPattern().Matches(line);
            if (commands.Count == 0)
                continue;

            var codes = TaskCodeDetector.Find(line[..commands[0].Index]);
            if (codes.Count == 0)
                continue;

            for (var i = 0; i < commands.Count; i++)
            {
                var start = commands[i].Index + commands[i].Length;
                var end = i + 1 < commands.Count ? commands[i + 1].Index : line.Length;
                var argument = line[start..end].Trim();
                var kind = commands[i].Groups[1].Value.ToLowerInvariant() switch
                {
                    "done" => SmartCommandKind.Done,
                    "comment" => SmartCommandKind.Comment,
                    _ => SmartCommandKind.Status
                };

                argument = kind switch
                {
                    SmartCommandKind.Status => argument.Trim('"', '«', '»', '\'', ' '),
                    SmartCommandKind.Comment => argument.Length <= MaxCommentLength ? argument : argument[..MaxCommentLength],
                    _ => ""
                };
                if (kind != SmartCommandKind.Done && argument.Length == 0)
                    continue;

                foreach (var code in codes)
                    result.Add(new SmartCommand(code, kind, kind == SmartCommandKind.Done ? null : argument));
            }
        }

        return result;
    }
}
