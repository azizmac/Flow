using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Features.PermissionSets;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>Наборы прав (docs/TZ_project_access.md §7, этап 4E): читают все, правит Owner.</summary>
[ApiController]
[Route("permission-sets")]
public class PermissionSetsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new PermissionSetListQuery(actor.Require()), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(SavePermissionSetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return StatusCode(StatusCodes.Status201Created, await mediator.Send(new PermissionSetCreateCommand(actor.Require(), request.Name,
                request.Description, request.BaseRole.ToDomainRole(), PermissionSetMapping.ToDomain(request.Permissions)), cancellationToken));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SavePermissionSetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(new PermissionSetUpdateCommand(actor.Require(), id, request.Name, request.Description,
                PermissionSetMapping.ToDomain(request.Permissions)), cancellationToken) is { } set ? Ok(set) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(new PermissionSetDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
