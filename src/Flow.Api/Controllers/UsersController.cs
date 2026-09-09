using Flow.Application.Features.Users;
using Flow.Application.Features.Users.Commands.UserActivateCommand;
using Flow.Application.Features.Users.Commands.UserChangeEmailCommand;
using Flow.Application.Features.Users.Commands.UserChangePasswordCommand;
using Flow.Application.Features.Users.Commands.UserChangeRoleCommand;
using Flow.Application.Features.Users.Commands.UserChangeUsernameCommand;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Application.Features.Users.Commands.UserRemoveLinkCommand;
using Flow.Application.Features.Users.Commands.UserSetLinkCommand;
using Flow.Application.Features.Users.Commands.UserUpdateProfileCommand;
using Flow.Application.Features.Users.Queries.UserGetByUsernameQuery;
using Flow.Application.Features.Users.Queries.UserGetMeQuery;
using Flow.Application.Features.Users.Queries.UserGetQuery;
using Flow.Application.Features.Users.Queries.UserListQuery;
using Flow.Application.Features.Users.Queries.UserSearchQuery;
using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Users;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Пользователи не удаляются (DELETE /users/{id} нет намеренно) — только деактивируются,
/// чтобы не терять историю назначений и упоминаний. См. docs/TZ_user.md.
/// </summary>
[ApiController]
[Route("users")]
public class UsersController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    /// <summary>Создаёт учётную запись в Flow.Auth (нужен начальный пароль) и профиль. 502 — Flow.Auth недоступен.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { Message = "Password is required." });

        try
        {
            var result = await mediator.Send(
                new UserCreateCommand(actor.Require(), request.Username, request.Email, request.FirstName, request.LastName, request.Password, request.Role?.ToDomainRole()),
                cancellationToken);

            if (result.IsConflict)
                return Conflict(new { Message = result.ConflictError });

            var response = result.Response!;
            return CreatedAtAction(nameof(GetUser), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var users = await mediator.Send(new UserListQuery(includeInactive), cancellationToken);
        return Ok(users);
    }

    /// <summary>Автодополнение для @упоминаний и выбора исполнителя. Только активные пользователи.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string q,
        [FromQuery] int limit = UserSearchQuery.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var users = await mediator.Send(new UserSearchQuery(q, limit), cancellationToken);
        return Ok(users);
    }

    /// <summary>Профиль текущего пользователя (claim sub). 401 — токен валиден, а профиля нет (удалён руками).</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        if (actor.ActorId is not { } actorId)
            return Unauthorized();

        var me = await mediator.Send(new UserGetMeQuery(actorId), cancellationToken);
        return me is null ? Unauthorized(new { Message = "Профиль для этой учётной записи не найден." }) : Ok(me);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await mediator.Send(new UserGetQuery(id), cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpGet("by-username/{username}")]
    public async Task<IActionResult> GetUserByUsername(string username, CancellationToken cancellationToken)
    {
        var user = await mediator.Send(new UserGetByUsernameQuery(username), cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> UpdateProfile(Guid id, UpdateUserProfileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(
                new UserUpdateProfileCommand(
                    actor.Require(),
                    id,
                    request.FirstName,
                    request.LastName,
                    request.JobTitle,
                    request.Bio,
                    request.PhoneNumber,
                    request.AvatarUrl),
                cancellationToken);

            return ToActionResult(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpPatch("{id:guid}/username")]
    public async Task<IActionResult> ChangeUsername(Guid id, ChangeUsernameRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new UserChangeUsernameCommand(actor.Require(), id, request.Username), cancellationToken);
            return ToActionResult(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpPatch("{id:guid}/email")]
    public async Task<IActionResult> ChangeEmail(Guid id, ChangeEmailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new UserChangeEmailCommand(actor.Require(), id, request.Email), cancellationToken);
            return ToActionResult(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Роль workspace. 403 — не позволяет роль actor'а (IPermissionService); 400 — последний Owner.</summary>
    [HttpPatch("{id:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid id, ChangeUserRoleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new UserChangeRoleCommand(actor.Require(), id, request.Role.ToDomainRole()), cancellationToken);
            return ToActionResult(result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>
    /// CurrentPassword задан — смена своего пароля; null — сброс чужого (только Owner, IPermissionService).
    /// Свой без текущего, неверный текущий или слабый новый пароль → 400.
    /// </summary>
    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> ChangePassword(Guid id, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(
                new UserChangePasswordCommand(actor.Require(), id, request.CurrentPassword, request.NewPassword),
                cancellationToken);

            return result.IsNotFound ? NotFound() : NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Добавляет ссылку или заменяет URL ссылки того же типа — поэтому PUT, а не POST.</summary>
    [HttpPut("{id:guid}/links/{type}")]
    public async Task<IActionResult> SetLink(Guid id, UserLinkType type, SetUserLinkRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new UserSetLinkCommand(actor.Require(), id, type, request.Url), cancellationToken);
            return ToActionResult(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete("{id:guid}/links/{type}")]
    public async Task<IActionResult> RemoveLink(Guid id, UserLinkType type, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new UserRemoveLinkCommand(actor.Require(), id, type), cancellationToken);
        return result.IsNotFound ? NotFound() : NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new UserDeactivateCommand(actor.Require(), id), cancellationToken);
            return result.IsNotFound ? NotFound() : NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new UserActivateCommand(actor.Require(), id), cancellationToken);
            return result.IsNotFound ? NotFound() : NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>404 / 409 / 200 — три исхода UserUpdateResult, общие для всех изменяющих эндпоинтов.</summary>
    private IActionResult ToActionResult(UserUpdateResult result)
    {
        if (result.IsNotFound)
            return NotFound();

        if (result.ConflictError is not null)
            return Conflict(new { Message = result.ConflictError });

        return Ok(result.Response);
    }
}
