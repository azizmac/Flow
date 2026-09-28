using Flow.Shared.Contracts.Scm;

namespace Flow.Client.Services;

/// <summary>Подписи интеграции с Git (docs/TZ_scm_integration.md): хостинги, состояния PR, подсказки по правам токена.</summary>
public static class ScmMeta
{
    public static readonly ScmProvider[] Providers = [ScmProvider.GitHub, ScmProvider.GitLab, ScmProvider.Gitea, ScmProvider.Forgejo];

    public static string Name(ScmProvider provider) => provider switch
    {
        ScmProvider.GitHub => "GitHub",
        ScmProvider.GitLab => "GitLab",
        ScmProvider.Gitea => "Gitea",
        _ => "Forgejo"
    };

    public static string StateLabel(ScmLinkState? state) => state switch
    {
        ScmLinkState.Open => "открыт",
        ScmLinkState.Draft => "черновик",
        ScmLinkState.Merged => "влит",
        ScmLinkState.Closed => "закрыт",
        _ => ""
    };

    /// <summary>CSS-модификатор чипа: влит — sage, открыт — акцент, закрыт и черновик — нейтрально.</summary>
    public static string StateClass(ScmLinkState? state) => state switch
    {
        ScmLinkState.Open => "open",
        ScmLinkState.Merged => "merged",
        _ => "muted"
    };

    /// <summary>Профиль flow-bot (Domain ScmBot.Id): им подписаны автопереходы без сопоставленного автора PR.</summary>
    public static readonly Guid BotId = new("00000000-0000-0000-0000-00000000f10b");

    /// <summary>«коммит a1b2c3d» → «коммиту a1b2c3d» после «по»; «PR #42» не склоняется.</summary>
    public static string SourceLabel(string source) =>
        source.StartsWith("коммит ", StringComparison.Ordinal) ? "коммиту " + source["коммит ".Length..] : source;

    public static string AuthName(ScmAuthKind kind) => kind == ScmAuthKind.GitHubApp ? "GitHub App" : "Токен";

    public static string DeliveryStatusName(ScmDeliveryStatus status) => status switch
    {
        ScmDeliveryStatus.Pending => "в очереди",
        ScmDeliveryStatus.Done => "разобрана",
        ScmDeliveryStatus.Failed => "ошибка",
        _ => "пропущена"
    };

    /// <summary>Событие доставки по-русски: имена хостингов (push, Merge Request Hook…) и задание дозагрузки истории.</summary>
    public static string EventName(ScmDeliveryResponse delivery) => delivery.IsBackfill
        ? "дозагрузка истории"
        : delivery.Event switch
        {
            "push" or "Push Hook" => "push",
            "pull_request" or "Merge Request Hook" => "pull request",
            "create" => "ветка создана",
            "delete" => "ветка удалена",
            var other => other
        };

    public static string TokenHint(ScmProvider provider) => provider switch
    {
        ScmProvider.GitHub => "Fine-grained токен: Metadata и Contents — чтение, Pull requests — чтение, Webhooks — запись. Или классический с правом repo.",
        ScmProvider.GitLab => "Токен доступа с правом api (личный, проекта или группы).",
        _ => "Токен с правами read:repository и write:repository (для вебхуков)."
    };

    public static string? TokenUrl(ScmProvider provider, string? baseUrl) => provider switch
    {
        ScmProvider.GitHub => "https://github.com/settings/personal-access-tokens/new",
        ScmProvider.GitLab when baseUrl is not null => $"{baseUrl.TrimEnd('/')}/-/user_settings/personal_access_tokens",
        _ when baseUrl is not null => $"{baseUrl.TrimEnd('/')}/user/settings/applications",
        _ => null
    };
}
