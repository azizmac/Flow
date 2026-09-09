using Flow.Auth.Data;
using Flow.Auth.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Flow.Auth.Security;

/// <summary>
/// Создаёт учётную запись базового пользователя из секции Bootstrap при первом запуске.
/// Хеш пароля пишется напрямую и CreateAsync вызывается без пароля: валидаторы пароля не применяются
/// (пароль "admin" из env допустим — это решение оператора), уникальность username/email — применяется.
/// Если запись с Bootstrap:Id уже есть, ничего не делает, даже если env изменился: пароль после первого входа
/// принадлежит пользователю. Профиль Owner с тем же Id сеет Flow.Api.
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

        if (await users.FindByIdAsync(bootstrap.Id.ToString()) is not null)
            return false;

        var user = new ApplicationUser
        {
            Id = bootstrap.Id,
            UserName = bootstrap.Username.Trim().ToLowerInvariant(),
            Email = bootstrap.Email.Trim().ToLowerInvariant(),
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        user.PasswordHash = hasher.HashPassword(user, bootstrap.Password);

        var result = await users.CreateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Bootstrap user could not be created: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        logger.LogInformation("Bootstrap user {Username} <{Email}> created with id {Id}", user.UserName, user.Email, user.Id);
        return true;
    }
}
