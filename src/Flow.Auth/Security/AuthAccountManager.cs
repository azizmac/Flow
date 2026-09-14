using Flow.Auth.Contracts;
using Flow.Auth.Data;
using Microsoft.AspNetCore.Identity;

namespace Flow.Auth.Security;

/// <summary>
/// In-process управление учётными записями поверх Identity — реализация публичного контракта модуля
/// <see cref="IAccountService"/>. Прямой аналог прежнего admin-API AccountsController: та же нормализация
/// username/email и тот же маппинг ошибок Identity (дубликат → UsernameTaken/EmailTaken, прочее → Invalid).
/// </summary>
public sealed class AuthAccountManager(UserManager<ApplicationUser> users) : IAccountService
{
    public async Task<AccountResult> CreateAsync(Guid id, string username, string email, string password, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return AccountResult.Invalid("Account id must not be empty.");

        if (await FindAsync(id) is not null)
            return AccountResult.Invalid($"Account {id} already exists.");

        var user = new ApplicationUser
        {
            Id = id,
            UserName = Normalize(username),
            Email = Normalize(email),
            EmailConfirmed = true,
            LockoutEnabled = true,
            MustChangePassword = true
        };

        var result = await users.CreateAsync(user, password);
        return result.Succeeded ? AccountResult.Success() : ToResult(result);
    }

    public async Task<AccountResult> ChangeUsernameAsync(Guid id, string username, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id);
        if (user is null)
            return AccountResult.Invalid($"Account {id} not found.");

        var normalized = Normalize(username);
        var existing = await users.FindByNameAsync(normalized);
        if (existing is not null && existing.Id != id)
            return AccountResult.UsernameTaken($"Username '{normalized}' is already taken.");

        var result = await users.SetUserNameAsync(user, normalized);
        return result.Succeeded ? AccountResult.Success() : ToResult(result);
    }

    public async Task<AccountResult> ChangeEmailAsync(Guid id, string email, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id);
        if (user is null)
            return AccountResult.Invalid($"Account {id} not found.");

        var normalized = Normalize(email);
        var existing = await users.FindByEmailAsync(normalized);
        if (existing is not null && existing.Id != id)
            return AccountResult.EmailTaken($"Email '{normalized}' is already taken.");

        var result = await users.SetEmailAsync(user, normalized);
        if (!result.Succeeded)
            return ToResult(result);

        // Подтверждения почты нет (SMTP — вне ТЗ), поэтому новый адрес сразу считается подтверждённым.
        user.EmailConfirmed = true;
        result = await users.UpdateAsync(user);
        return result.Succeeded ? AccountResult.Success() : ToResult(result);
    }

    /// <summary>
    /// С currentPassword — смена своего пароля (снимает MustChangePassword); без него — сброс Owner'ом
    /// (ставит MustChangePassword: новый пароль задан не самим человеком). Оба меняют security stamp.
    /// </summary>
    public async Task<AccountResult> ChangePasswordAsync(Guid id, string? currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id);
        if (user is null)
            return AccountResult.Invalid($"Account {id} not found.");

        IdentityResult result;
        if (currentPassword is not null)
        {
            result = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        }
        else
        {
            result = await users.RemovePasswordAsync(user);
            if (result.Succeeded)
                result = await users.AddPasswordAsync(user, newPassword);
        }

        if (!result.Succeeded)
            return ToResult(result);

        user.MustChangePassword = currentPassword is null;
        result = await users.UpdateAsync(user);
        return result.Succeeded ? AccountResult.Success() : ToResult(result);
    }

    /// <summary>Деактивация: постоянный lockout — вход и refresh отказываются (см. AuthorizationController).</summary>
    public async Task DisableAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id) ?? throw new InvalidOperationException($"Account {id} not found.");

        await users.SetLockoutEnabledAsync(user, true);
        var result = await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        await users.UpdateSecurityStampAsync(user);
    }

    public async Task EnableAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id) ?? throw new InvalidOperationException($"Account {id} not found.");

        var result = await users.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        await users.ResetAccessFailedCountAsync(user);
    }

    private Task<ApplicationUser?> FindAsync(Guid id) => users.FindByIdAsync(id.ToString());

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    /// <summary>Дубликаты username/email → конфликт (Application транслирует в 409), остальное → Invalid (400).</summary>
    private static AccountResult ToResult(IdentityResult result)
    {
        var message = string.Join(" ", result.Errors.Select(e => e.Description));
        var isConflict = result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.DuplicateEmail));
        if (!isConflict)
            return AccountResult.Invalid(message);

        return message.Contains("email", StringComparison.OrdinalIgnoreCase)
            ? AccountResult.EmailTaken(message)
            : AccountResult.UsernameTaken(message);
    }
}
