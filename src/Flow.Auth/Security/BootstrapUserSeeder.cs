using Flow.Auth.Data;
using Flow.Auth.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Flow.Auth.Security;

/// <summary>
/// Создаёт учётную запись базового пользователя из секции Bootstrap при первом запуске.
/// Хеш пароля пишется напрямую и CreateAsync вызывается без пароля: валидаторы пароля не применяются
/// (пароль "admin" из env допустим — это решение оператора), уникальность username/email — применяется.
/// Пароль помечается как подлежащий смене (<see cref="ApplicationUser.MustChangePassword"/>): пока он равен
/// bootstrap-паролю, войти в приложение нельзя — только сменить. Если запись уже есть, а пароль всё ещё
/// bootstrap-овский (базы, созданные до этого правила), флаг ставится при старте. Профиль Owner с тем же Id сеет Flow.Api.
/// </summary>
public sealed class BootstrapUserSeeder(
    UserManager<ApplicationUser> users,
    IPasswordHasher<ApplicationUser> hasher,
    IOptions<BootstrapOptions> options,
    ILogger<BootstrapUserSeeder> logger)
{
    /// <summary>Возвращает true, если пользователь был создан этим вызовом.</summary>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        var bootstrap = options.Value;
        if (!bootstrap.Enabled)
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        var existing = await users.FindByIdAsync(bootstrap.Id.ToString());
        if (existing is not null)
        {
            await FlagDefaultPasswordAsync(existing, bootstrap.Password);
            return false;
        }

        var user = new ApplicationUser
        {
            Id = bootstrap.Id,
            UserName = bootstrap.Username.Trim().ToLowerInvariant(),
            Email = bootstrap.Email.Trim().ToLowerInvariant(),
            EmailConfirmed = true,
            LockoutEnabled = true,
            MustChangePassword = true
        };
        user.PasswordHash = hasher.HashPassword(user, bootstrap.Password);

        var result = await users.CreateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Bootstrap user could not be created: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        logger.LogInformation("Bootstrap user {Username} <{Email}> created with id {Id}; password must be changed on first login", user.UserName, user.Email, user.Id);
        return true;
    }

    private async Task FlagDefaultPasswordAsync(ApplicationUser user, string defaultPassword)
    {
        if (user.MustChangePassword || user.PasswordHash is null)
            return;

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, defaultPassword) == PasswordVerificationResult.Failed)
            return;

        user.MustChangePassword = true;
        await users.UpdateAsync(user);
        logger.LogWarning("Bootstrap user {Username} still has the default password — change required on next login", user.UserName);
    }
}
