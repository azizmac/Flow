using Flow.Domain.Entities;
using Flow.Shared.Contracts.Users;
using DomainLinkType = Flow.Domain.Entities.UserLinkType;
using SharedLinkType = Flow.Shared.Contracts.Users.UserLinkType;

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
        user.CreatedAt);

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
