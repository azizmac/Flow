namespace Flow.Shared.Contracts.Users;

/// <summary>Зеркало Flow.Domain.Entities.UserLinkType — Flow.Shared намеренно не ссылается на Flow.Domain.</summary>
public enum UserLinkType
{
    GitHub = 0,
    GitLab = 1,
    Telegram = 2,
    LinkedIn = 3,
    Website = 4,
    Other = 5
}
