using Flow.Application.Abstractions;
using Flow.Application.Features.Agents.Commands.AgentTestAskCommand;
using Flow.Shared.Contracts.Agents;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>Временный HTTP-вход для проверки подключения Flow к OpenCode.</summary>
[ApiController]
[Route("agents")]
public class AgentsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    /// <summary>Отправляет вопрос в OpenCode для указанного каталога общего workspace.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test(AgentTestRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new AgentTestAskCommand(actor.Require(), request.WorkspaceDirectory, request.Question),
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { ex.Message });
        }
    }
}
