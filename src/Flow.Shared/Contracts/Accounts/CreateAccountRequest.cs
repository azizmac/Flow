namespace Flow.Shared.Contracts.Accounts;

/// <summary>
/// POST /accounts (admin-API Flow.Auth). Id задаёт вызывающий (Flow.Api), чтобы учётная запись и профиль
/// User в Flow.Api имели один и тот же ключ без второго запроса. MustChangePassword — пароль задан админом,
/// человек обязан сменить его при первом входе (по умолчанию так; false — только для тестов).
/// </summary>
public sealed record CreateAccountRequest(Guid Id, string Username, string Email, string Password, bool MustChangePassword = true);
