using Flow.Application.Abstractions;
using Flow.Application.Features.Groups;
using Flow.Shared.Contracts.Users;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>Группы людей (docs/TZ_project_access.md §4, этап 4C): читают все, меняют глобальные Admin+.</summary>
[ApiController]
[Route("groups")]
public class GroupsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GroupListQuery(actor.Require()), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(SaveGroupRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return StatusCode(StatusCodes.Status201Created,
                await mediator.Send(new GroupCreateCommand(actor.Require(), request.Name, request.Description), cancellationToken));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpPatch("{id:guid}")]
    public Task<IActionResult> Update(Guid id, SaveGroupRequest request, CancellationToken cancellationToken) =>
        Send(() => mediator.Send(new GroupUpdateCommand(actor.Require(), id, request.Name, request.Description), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new GroupDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    [HttpPut("{id:guid}/members/{userId:guid}")]
    public Task<IActionResult> AddMember(Guid id, Guid userId, CancellationToken cancellationToken) =>
        Send(() => mediator.Send(new GroupMemberSetCommand(actor.Require(), id, userId, true), cancellationToken));

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken) =>
        Send(() => mediator.Send(new GroupMemberSetCommand(actor.Require(), id, userId, false), cancellationToken));

    private async Task<IActionResult> Send(Func<Task<GroupResponse?>> send)
    {
        try
        {
            return await send() is { } group ? Ok(group) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
