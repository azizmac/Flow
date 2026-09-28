namespace Flow.Domain.Entities.GitIntegration;

/// <summary>
/// Привязка репозитория к проекту: коды задач связываются только в привязанных проектах — иначе публичный
/// репозиторий с «fix WEB-12» прицепил бы чужой текст к задаче приватного проекта. Монорепозиторий — несколько привязок.
/// </summary>
public sealed class ScmRepositoryBoard
{
    public Guid RepositoryId { get; private set; }

    public Guid BoardId { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private ScmRepositoryBoard()
    {
        // EF Core
    }

    /// <summary>Автопереход (этап 5C): PR открыт → этот статус проекта; null — выключен.</summary>
    public Guid? OnPullRequestOpenedStatusId { get; private set; }

    /// <summary>Автопереход: PR влит в ветку по умолчанию → этот статус; null — выключен.</summary>
    public Guid? OnPullRequestMergedStatusId { get; private set; }

    /// <summary>Смарт-коммиты (#done, #comment, #status) в push'ах в ветку по умолчанию.</summary>
    public bool SmartCommits { get; private set; }

    /// <summary>Комментарий в новом PR со ссылкой на задачу (этап 5D) — от имени токена подключения.</summary>
    public bool CommentOnPullRequests { get; private set; }

    public static ScmRepositoryBoard Create(Guid repositoryId, Guid boardId, Guid createdById) =>
        new() { RepositoryId = repositoryId, BoardId = boardId, CreatedById = createdById, CreatedAt = DateTime.UtcNow };

    /// <summary>Настройки автоматизации привязки. Принадлежность статусов проекту проверяет хендлер — он видит доску.</summary>
    public void Configure(Guid? onPullRequestOpenedStatusId, Guid? onPullRequestMergedStatusId, bool smartCommits, bool commentOnPullRequests = false)
    {
        OnPullRequestOpenedStatusId = onPullRequestOpenedStatusId;
        OnPullRequestMergedStatusId = onPullRequestMergedStatusId;
        SmartCommits = smartCommits;
        CommentOnPullRequests = commentOnPullRequests;
    }
}
