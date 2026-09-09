namespace Flow.Shared.Contracts.Users;

/// <summary>Role и Status — из Flow.Api (не из токена); IsActive = Status != Deactivated, оставлен для совместимости.</summary>
public sealed record UserResponse(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? AvatarUrl,
    string? JobTitle,
    string? Bio,
    string? PhoneNumber,
    IReadOnlyList<UserLinkResponse> Links,
    bool IsActive,
    DateTime CreatedAt,
    UserRole Role,
    UserStatus Status,
    DateTime? StatusChangedAt);
