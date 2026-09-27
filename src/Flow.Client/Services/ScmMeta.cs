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
