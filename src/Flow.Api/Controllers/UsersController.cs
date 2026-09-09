using Flow.Application.Features.Users;
using Flow.Application.Features.Users.Commands.UserActivateCommand;
using Flow.Application.Features.Users.Commands.UserChangeEmailCommand;
using Flow.Application.Features.Users.Commands.UserChangeUsernameCommand;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Application.Features.Users.Commands.UserRemoveLinkCommand;
using Flow.Application.Features.Users.Commands.UserSetLinkCommand;
using Flow.Application.Features.Users.Commands.UserUpdateProfileCommand;
using Flow.Application.Features.Users.Queries.UserGetByUsernameQuery;
using Flow.Application.Features.Users.Queries.UserGetQuery;
using Flow.Application.Features.Users.Queries.UserListQuery;
using Flow.Application.Features.Users.Queries.UserSearchQuery;
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
public class UsersController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(
                new UserCreateCommand(request.Username, request.Email, request.FirstName, request.LastName, request.Password ?? string.Empty),
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
            var result = await mediator.Send(new UserChangeUsernameCommand(id, request.Username), cancellationToken);
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
            var result = await mediator.Send(new UserChangeEmailCommand(id, request.Email), cancellationToken);
            return ToActionResult(result);
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
            var result = await mediator.Send(new UserSetLinkCommand(id, type, request.Url), cancellationToken);
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
        var result = await mediator.Send(new UserRemoveLinkCommand(id, type), cancellationToken);
        return result.IsNotFound ? NotFound() : NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new UserDeactivateCommand(id), cancellationToken);
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
            var result = await mediator.Send(new UserActivateCommand(id), cancellationToken);
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
