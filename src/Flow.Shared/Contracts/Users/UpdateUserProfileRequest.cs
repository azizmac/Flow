namespace Flow.Shared.Contracts.Users;

/// <summary>
/// PATCH-семантика: null — поле не трогать. Пустая строка в необязательном поле (JobTitle, Bio, PhoneNumber, AvatarUrl)
/// очищает его. Username и Email меняются отдельными эндпоинтами, т.к. могут ответить 409.
/// </summary>
public sealed record UpdateUserProfileRequest(
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? Bio,
    string? PhoneNumber,
    string? AvatarUrl);
