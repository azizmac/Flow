namespace Flow.Shared.Contracts.Users;

/// <summary>Зеркало Flow.Domain.Entities.UserStatus. IsActive в ответах остаётся: это Status != Deactivated.</summary>
public enum UserStatus
{
    Invited = 0,
    Active = 1,
    Deactivated = 2
}
