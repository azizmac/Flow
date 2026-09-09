namespace Flow.Shared.Contracts.Accounts;

/// <summary>IsDisabled = постоянная блокировка входа (деактивация в Flow.Api), не временный lockout после неудачных попыток.</summary>
public sealed record AccountResponse(Guid Id, string Username, string Email, bool IsDisabled);
