namespace Flow.Domain.Entities.GitIntegration;

// Интеграция с Git-хостингами (docs/TZ_scm_integration.md, этап 5A). Forgejo = Gitea по протоколу: один разбор,
// различие — заголовки подписи и доставки. Секреты хранятся зашифрованными (IDataProtector, purpose Flow.Scm):
// домен видит только непрозрачную строку.

public enum ScmProvider
{
    GitHub = 0,
    GitLab = 1,
    Gitea = 2,
    Forgejo = 3
}

/// <summary>Чем подключение входит в API: личный/сервисный токен или GitHub App (закрытый ключ + установка, этап 5B).</summary>
public enum ScmAuthKind
{
    Token = 0,
    GitHubApp = 1
}

public enum ScmLinkKind
{
    Branch = 0,
    Commit = 1,
    PullRequest = 2
}

public enum ScmLinkState
{
    Open = 0,
    Draft = 1,
    Merged = 2,
    Closed = 3
}

public enum ScmDeliveryStatus
{
    Pending = 0,
    Done = 1,
    Failed = 2,
    Ignored = 3
}
