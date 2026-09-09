namespace Flow.Shared.Contracts.Users;

/// <summary>Password — начальный пароль учётной записи в Flow.Auth (≥ 8 символов); человек сменит его в профиле.</summary>
public sealed record CreateUserRequest(string Username, string Email, string FirstName, string LastName, string? Password = null);
