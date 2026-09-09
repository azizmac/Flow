namespace Flow.Shared.Contracts.Users;

/// <summary>Зеркало Flow.Domain.Entities.UserRole (Shared не ссылается на Domain). Порядок = сила роли, сравнивать через >=.</summary>
public enum UserRole
{
    Reader = 0,
    Member = 1,
    Developer = 2,
    Admin = 3,
    Owner = 4
}
