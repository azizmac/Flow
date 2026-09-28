using Flow.Application.Abstractions;
using Flow.Application.Features.Templates;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>Шаблоны проектов и перенос конфигурации (docs/TZ_workflow_config.md §4, этап 3F).</summary>
[ApiController]
public class BoardTemplatesController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet("board-templates")]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new BoardTemplateListQuery(actor.Require()), cancellationToken));

    [HttpPost("boards/{id:guid}/save-as-template")]
    public async Task<IActionResult> Save(Guid id, SaveBoardTemplateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var template = await mediator.Send(new BoardTemplateSaveCommand(actor.Require(), id, request.Name, request.Description, request.IncludeTasks),
                cancellationToken);
            return template is null ? NotFound() : StatusCode(StatusCodes.Status201Created, template);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete("board-templates/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(new BoardTemplateDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpPost("boards/{id:guid}/apply-config/preview")]
    public async Task<IActionResult> Preview(Guid id, ApplyBoardConfigRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await mediator.Send(new BoardApplyConfigPreviewQuery(actor.Require(), id, request.Targets, request.Parts), cancellationToken);
            return preview is null ? NotFound() : Ok(preview);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpPost("boards/{id:guid}/apply-config")]
    public async Task<IActionResult> Apply(Guid id, ApplyBoardConfigRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new BoardApplyConfigCommand(actor.Require(), id, request.Targets, request.Parts), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
