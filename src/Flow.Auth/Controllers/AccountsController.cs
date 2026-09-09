using Flow.Auth.Data;
using Flow.Auth.Security;
using Flow.Shared.Contracts.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Auth.Controllers;

/// <summary>
/// Admin-API для Flow.Api (client_credentials клиента flow-api, scope auth:admin). Кто имеет право
/// на каждое действие (Owner/Admin по docs/TZ_user_roles.md), решает Flow.Api — здесь доверие к flow-api целиком.
/// Username/email/пароль/блокировка — собственность этого сервиса; Flow.Api держит копию username/email.
/// </summary>
[ApiController]
[Route("accounts")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AccountsController(UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountRequest request)
    {
        if (request.Id == Guid.Empty)
            return BadRequest(new { Message = "Account id must not be empty." });

        if (await users.FindByIdAsync(request.Id.ToString()) is not null)
            return Conflict(new { Message = $"Account {request.Id} already exists." });

        var user = new ApplicationUser
        {
            Id = request.Id,
            UserName = Normalize(request.Username),
            Email = Normalize(request.Email),
            EmailConfirmed = true,
            LockoutEnabled = true
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return ToError(result);

        return CreatedAtAction(nameof(Get), new { id = user.Id }, ToResponse(user));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is null ? NotFound() : Ok(ToResponse(user));
    }

    [HttpPatch("{id:guid}/username")]
    public async Task<IActionResult> ChangeUsername(Guid id, ChangeAccountUsernameRequest request)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var result = await users.SetUserNameAsync(user, Normalize(request.Username));
        return result.Succeeded ? NoContent() : ToError(result);
    }

    [HttpPatch("{id:guid}/email")]
    public async Task<IActionResult> ChangeEmail(Guid id, ChangeAccountEmailRequest request)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var result = await users.SetEmailAsync(user, Normalize(request.Email));
        if (!result.Succeeded)
            return ToError(result);

        // Подтверждения почты нет (SMTP — вне ТЗ), поэтому новый адрес сразу считается подтверждённым.
        user.EmailConfirmed = true;
        result = await users.UpdateAsync(user);
        return result.Succeeded ? NoContent() : ToError(result);
    }

    /// <summary>С CurrentPassword — смена своего пароля; без него — сброс (Owner). Оба меняют security stamp.</summary>
    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> ChangePassword(Guid id, ChangeAccountPasswordRequest request)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        IdentityResult result;
        if (request.CurrentPassword is not null)
        {
            result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        }
        else
        {
            result = await users.RemovePasswordAsync(user);
            if (result.Succeeded)
                result = await users.AddPasswordAsync(user, request.NewPassword);
        }

        return result.Succeeded ? NoContent() : ToError(result);
    }

    /// <summary>Деактивация: постоянный lockout — вход и refresh отказываются (см. AuthorizationController).</summary>
    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        await users.SetLockoutEnabledAsync(user, true);
        var result = await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        if (!result.Succeeded)
            return ToError(result);

        await users.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    [HttpPost("{id:guid}/enable")]
    public async Task<IActionResult> Enable(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var result = await users.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded)
            return ToError(result);

        await users.ResetAccessFailedCountAsync(user);
        return NoContent();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static AccountResponse ToResponse(ApplicationUser user) =>
        new(user.Id, user.UserName!, user.Email!, IsDisabled(user));

    /// <summary>Постоянная блокировка (disable) отличается от временной (5 неудачных попыток) горизонтом LockoutEnd.</summary>
    internal static bool IsDisabled(ApplicationUser user) =>
        user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow.AddYears(100);

    /// <summary>Дубликаты username/email → 409 (Flow.Api транслирует в UsernameTaken/EmailTaken), остальное → 400.</summary>
    private IActionResult ToError(IdentityResult result)
    {
        var message = string.Join(" ", result.Errors.Select(e => e.Description));
        var isConflict = result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.DuplicateEmail));
        return isConflict ? Conflict(new { Message = message }) : BadRequest(new { Message = message });
    }
}
