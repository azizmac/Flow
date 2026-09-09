namespace Flow.Shared.Contracts.Accounts;

/// <summary>
/// IsDisabled = постоянная блокировка входа (деактивация в Flow.Api), не временный lockout после неудачных попыток.
/// MustChangePassword = пароль задан не самим человеком; до смены Flow.Auth не выдаёт токен.
/// </summary>
public sealed record AccountResponse(Guid Id, string Username, string Email, bool IsDisabled, bool MustChangePassword);
