namespace Flow.Shared.Contracts.Accounts;

/// <summary>CurrentPassword = null — сброс без проверки текущего (Owner сбрасывает чужой пароль; право проверяет Flow.Api).</summary>
public sealed record ChangeAccountPasswordRequest(string? CurrentPassword, string NewPassword);
