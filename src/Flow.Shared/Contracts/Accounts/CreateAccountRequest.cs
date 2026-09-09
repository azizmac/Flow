namespace Flow.Shared.Contracts.Accounts;

/// <summary>
/// POST /accounts (admin-API Flow.Auth). Id задаёт вызывающий (Flow.Api), чтобы учётная запись и профиль
/// User в Flow.Api имели один и тот же ключ без второго запроса.
/// </summary>
public sealed record CreateAccountRequest(Guid Id, string Username, string Email, string Password);
