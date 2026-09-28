using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Features.Scm;

/// <summary>Адреса и тексты для хостинга: ссылка на ветку и комментарий «задача Flow» (этап 5D).</summary>
public static class ScmUrls
{
    public static string Branch(ScmProvider provider, string webUrl, string branch)
    {
        var escaped = string.Join('/', branch.Split('/').Select(Uri.EscapeDataString));
        return provider switch
        {
            ScmProvider.GitHub => $"{webUrl}/tree/{escaped}",
            ScmProvider.GitLab => $"{webUrl}/-/tree/{escaped}",
            _ => $"{webUrl}/src/branch/{escaped}"
        };
    }

    /// <summary>Markdown одинаково понимают все четыре хостинга; название — недоверенный для хостинга текст, режем скобки.</summary>
    public static string TaskComment(string code, string title, string url) =>
        $"Задача Flow: [{code} {title.Replace('[', '(').Replace(']', ')')}]({url})";

    /// <summary>Имя ветки git: латиница, цифры, «.», «_», «-», «/»; без «..», пробелов и «/» по краям.</summary>
    public static string ValidateBranch(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > 200 || trimmed.StartsWith('-') || trimmed.StartsWith('/') || trimmed.EndsWith('/')
            || trimmed.EndsWith(".lock", StringComparison.Ordinal) || trimmed.Contains("..") || trimmed.Contains("//")
            || !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '/'))
            throw new ArgumentException($"«{name}» не годится в имя ветки git.");
        return trimmed;
    }
}
