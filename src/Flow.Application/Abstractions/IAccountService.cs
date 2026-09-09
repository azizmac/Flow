namespace Flow.Application.Abstractions;

/// <summary>
/// Учётные записи (username, email, пароль, блокировка входа) живут в Flow.Auth — это клиент его admin-API.
/// Flow.Auth — источник истины для username/email: команды сначала меняют учётную запись, и только при успехе —
/// копию в Users. Реализация — Flow.Infrastructure (AuthAccountService); недоступность сервиса →
/// <see cref="Exceptions.AuthUnavailableException"/> → 502.
/// </summary>
public interface IAccountService
{
    /// <summary>Id задаёт вызывающий: учётная запись и профиль User должны совпадать по ключу.</summary>
    Task<AccountResult> CreateAsync(Guid id, string username, string email, string password, CancellationToken cancellationToken);

    Task<AccountResult> ChangeUsernameAsync(Guid id, string username, CancellationToken cancellationToken);

    Task<AccountResult> ChangeEmailAsync(Guid id, string email, CancellationToken cancellationToken);

    /// <summary>currentPassword = null — сброс без проверки текущего (право Owner проверяет Application).</summary>
    Task<AccountResult> ChangePasswordAsync(Guid id, string? currentPassword, string newPassword, CancellationToken cancellationToken);

    /// <summary>Постоянная блокировка входа и refresh-токенов (деактивация).</summary>
    Task DisableAsync(Guid id, CancellationToken cancellationToken);

    Task EnableAsync(Guid id, CancellationToken cancellationToken);
}

public enum AccountResultStatus
{
    Success,
    UsernameTaken,
    EmailTaken,

    /// <summary>Flow.Auth отверг ввод (слабый пароль, неверный текущий пароль, формат) — Error содержит его текст.</summary>
    Invalid
}

public sealed record AccountResult(AccountResultStatus Status, string? Error = null)
{
    public bool IsSuccess => Status == AccountResultStatus.Success;

    public static AccountResult Success() => new(AccountResultStatus.Success);

    public static AccountResult UsernameTaken(string message) => new(AccountResultStatus.UsernameTaken, message);

    public static AccountResult EmailTaken(string message) => new(AccountResultStatus.EmailTaken, message);

    public static AccountResult Invalid(string message) => new(AccountResultStatus.Invalid, message);
}
