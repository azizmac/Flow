using Flow.Domain.Entities;
using Flow.Shared.Contracts.Users;
using DomainLinkType = Flow.Domain.Entities.UserLinkType;
using DomainRole = Flow.Domain.Entities.UserRole;
using DomainStatus = Flow.Domain.Entities.UserStatus;
using SharedLinkType = Flow.Shared.Contracts.Users.UserLinkType;
using SharedRole = Flow.Shared.Contracts.Users.UserRole;
using SharedStatus = Flow.Shared.Contracts.Users.UserStatus;

namespace Flow.Application.Features.Users;

public static class UserMappingExtensions
{
    public static UserResponse ToResponse(this User user) => new(
        user.Id,
        user.Username,
        user.Email,
        user.FirstName,
        user.LastName,
        user.FullName,
        user.AvatarUrl,
        user.JobTitle,
        user.Bio,
        user.PhoneNumber,
        user.Links
            .OrderBy(l => l.Type)
            .Select(l => new UserLinkResponse(l.Type.ToResponseLinkType(), l.Url))
            .ToList(),
        user.IsActive,
        user.CreatedAt,
        user.Role.ToResponseRole(),
        user.Status.ToResponseStatus(),
        user.StatusChangedAt);

    /// <summary>Значения enum'ов совпадают (зеркала), поэтому маппинг — приведение с проверкой, что значение известно.</summary>
    public static SharedRole ToResponseRole(this DomainRole role) =>
        Enum.IsDefined(role) ? (SharedRole)(int)role : throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown UserRole.");

    public static DomainRole ToDomainRole(this SharedRole role) =>
        Enum.IsDefined(role) ? (DomainRole)(int)role : throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown UserRole.");

    public static SharedStatus ToResponseStatus(this DomainStatus status) =>
        Enum.IsDefined(status) ? (SharedStatus)(int)status : throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown UserStatus.");

    /// <summary>Два разных enum: Flow.Shared намеренно не ссылается на Flow.Domain (как со StatusType).</summary>
    public static SharedLinkType ToResponseLinkType(this DomainLinkType type) => type switch
    {
        DomainLinkType.GitHub => SharedLinkType.GitHub,
        DomainLinkType.GitLab => SharedLinkType.GitLab,
        DomainLinkType.Telegram => SharedLinkType.Telegram,
        DomainLinkType.LinkedIn => SharedLinkType.LinkedIn,
        DomainLinkType.Website => SharedLinkType.Website,
        DomainLinkType.Other => SharedLinkType.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown UserLinkType.")
    };

    public static DomainLinkType ToDomainLinkType(this SharedLinkType type) => type switch
    {
        SharedLinkType.GitHub => DomainLinkType.GitHub,
        SharedLinkType.GitLab => DomainLinkType.GitLab,
        SharedLinkType.Telegram => DomainLinkType.Telegram,
        SharedLinkType.LinkedIn => DomainLinkType.LinkedIn,
        SharedLinkType.Website => DomainLinkType.Website,
        SharedLinkType.Other => DomainLinkType.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown UserLinkType.")
    };
}
