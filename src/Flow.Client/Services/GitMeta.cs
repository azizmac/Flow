using Flow.Shared.Contracts.GitIntegration;

namespace Flow.Client.Services;

/// <summary>Подписи интеграции с Git (docs/TZ_git_integration.md): хостинги, состояния PR, подсказки по правам токена.</summary>
public static class GitMeta
{
    public static readonly GitProvider[] Providers = [GitProvider.GitHub, GitProvider.GitLab, GitProvider.Gitea, GitProvider.Forgejo];

    public static string Name(GitProvider provider) => provider switch
    {
        GitProvider.GitHub => "GitHub",
        GitProvider.GitLab => "GitLab",
        GitProvider.Gitea => "Gitea",
        _ => "Forgejo"
    };

    public static string StateLabel(GitDevelopmentLinkState? state) => state switch
    {
        GitDevelopmentLinkState.Open => "открыт",
        GitDevelopmentLinkState.Draft => "черновик",
        GitDevelopmentLinkState.Merged => "влит",
        GitDevelopmentLinkState.Closed => "закрыт",
        _ => ""
    };

    /// <summary>CSS-модификатор чипа: влит — sage, открыт — акцент, закрыт и черновик — нейтрально.</summary>
    public static string StateClass(GitDevelopmentLinkState? state) => state switch
    {
        GitDevelopmentLinkState.Open => "open",
        GitDevelopmentLinkState.Merged => "merged",
        _ => "muted"
    };

    /// <summary>Профиль flow-bot (Domain GitIntegrationBot.Id): им подписаны автопереходы без сопоставленного автора PR.</summary>
    public static readonly Guid BotId = new("00000000-0000-0000-0000-00000000f10b");

    /// <summary>«коммит a1b2c3d» → «коммиту a1b2c3d» после «по»; «PR #42» не склоняется.</summary>
    public static string SourceLabel(string source) =>
        source.StartsWith("коммит ", StringComparison.Ordinal) ? "коммиту " + source["коммит ".Length..] : source;

    public static string AuthName(GitAuthenticationKind kind) => kind == GitAuthenticationKind.GitHubApp ? "GitHub App" : "Токен";

    public static string DeliveryStatusName(GitIntegrationJobStatus status) => status switch
    {
        GitIntegrationJobStatus.Pending => "в очереди",
        GitIntegrationJobStatus.Done => "разобрана",
        GitIntegrationJobStatus.Failed => "ошибка",
        _ => "пропущена"
    };

    /// <summary>Событие доставки по-русски: имена хостингов (push, Merge Request Hook…) и задание дозагрузки истории.</summary>
    public static string EventName(GitIntegrationJobResponse delivery) => delivery.IsBackfill
        ? "дозагрузка истории"
        : delivery.Event switch
        {
            "push" or "Push Hook" => "push",
            "pull_request" or "Merge Request Hook" => "pull request",
            "create" => "ветка создана",
            "delete" => "ветка удалена",
            var other => other
        };

    public static string TokenHint(GitProvider provider) => provider switch
    {
        GitProvider.GitHub => "Fine-grained токен: Metadata и Contents — чтение, Pull requests — чтение, Webhooks — запись. Или классический с правом repo.",
        GitProvider.GitLab => "Токен доступа с правом api (личный, проекта или группы).",
        _ => "Токен с правами read:repository и write:repository (для вебхуков)."
    };

    public static string? TokenUrl(GitProvider provider, string? baseUrl) => provider switch
    {
        GitProvider.GitHub => "https://github.com/settings/personal-access-tokens/new",
        GitProvider.GitLab when baseUrl is not null => $"{baseUrl.TrimEnd('/')}/-/user_settings/personal_access_tokens",
        _ when baseUrl is not null => $"{baseUrl.TrimEnd('/')}/user/settings/applications",
        _ => null
    };
}
