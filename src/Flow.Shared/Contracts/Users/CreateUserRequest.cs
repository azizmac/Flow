namespace Flow.Shared.Contracts.Users;

/// <summary>
/// Password — начальный пароль учётной записи в Flow.Auth (≥ 8 символов); человек сменит его в профиле.
/// Role — по умолчанию Member; Admin может выдать только роли ниже Admin, Owner — любую.
/// </summary>
public sealed record CreateUserRequest(string Username, string Email, string FirstName, string LastName, string? Password = null, UserRole? Role = null);
