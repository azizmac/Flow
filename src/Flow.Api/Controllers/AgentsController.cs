using Flow.Application.Abstractions;
using Flow.Application.Features.Agents.Commands.AgentTestAskCommand;
using Flow.Application.Features.Agents.Commands.TaskRequirementsReviewCommand;
using Flow.Shared.Contracts.Agents;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>Запросы к агенту Flow для анализа исходного кода и требований задачи.</summary>
[ApiController]
[Route("agents")]
public class AgentsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    /// <summary>Проверяет черновик требований по готовым ревизиям репозиториев проекта.</summary>
    [HttpPost("task-requirements")]
    [ProducesResponseType<TaskRequirementsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ReviewTaskRequirements(TaskRequirementsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await mediator.Send(new TaskRequirementsReviewCommand(
                actor.Require(), request.BoardId, request.Title, request.Description, request.TaskId), cancellationToken));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new ApiError(ex.Message));
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ApiError(
                "Не удалось получить рекомендации от OpenCode. Проверьте доступность агента и локальной модели."));
        }
        catch (Exception ex) when (ex is TimeoutException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ApiError(
                "Агент не завершил анализ за отведённое время. Попробуйте повторить запрос."));
        }
    }

    /// <summary>Отправляет вопрос в OpenCode для готовой ревизии подключённого репозитория.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test(AgentTestRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new AgentTestAskCommand(actor.Require(), request.RepositoryId, request.Question),
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
        catch (TimeoutException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ApiError(
                "Агент не завершил анализ за отведённое время. Попробуйте повторить запрос."));
        }
    }
}
