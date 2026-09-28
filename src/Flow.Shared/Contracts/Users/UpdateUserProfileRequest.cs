namespace Flow.Shared.Contracts.Users;

/// <summary>
/// PATCH-семантика: null — поле не трогать. Пустая строка в необязательном поле (JobTitle, Bio, PhoneNumber)
/// очищает его. Username и Email меняются отдельными эндпоинтами, т.к. могут ответить 409.
/// Аватара здесь нет: его не вписывают адресом, а загружают картинкой — PUT /users/me/avatar.
/// </summary>
public sealed record UpdateUserProfileRequest(
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? Bio,
    string? PhoneNumber);
