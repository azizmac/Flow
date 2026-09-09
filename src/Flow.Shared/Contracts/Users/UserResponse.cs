namespace Flow.Shared.Contracts.Users;

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
    DateTime CreatedAt);
