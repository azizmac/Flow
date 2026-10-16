using Flow.Application.Abstractions;
using Flow.Application.Features.Agents.Settings;
using Flow.Shared.Contracts.Agents;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Инфраструктура агента: провайдеры моделей, ключи и модель по умолчанию для OpenCode — глобальные Admin+.
/// Ключ API принимается и никогда не отдаётся обратно (в ответах HasKey и KeyHint). Сохранённое сразу уходит в OpenCode:
/// ключ провайдера из каталога действует без перезапуска, кастомный провайдер — после перезапуска OpenCode
/// (RestartRequired). OpenCode недоступен — не ошибка: причина в ApplyError, передачу повторит синхронизация.
/// </summary>
[ApiController]
public class AgentSettingsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    /// <summary>Отвечает ли OpenCode, какие провайдеры он видит и на какой модели Flow открывает сессии.</summary>
    [HttpGet("agents/status")]
    [ProducesResponseType<AgentStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new AgentStatusQuery(actor.Require()), cancellationToken));

    /// <summary>Каталог провайдеров OpenCode с моделями; OpenCode недоступен — 400.</summary>
    [HttpGet("agents/catalog")]
    [ProducesResponseType<IReadOnlyList<AgentCatalogProviderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Catalog(CancellationToken cancellationToken) =>
        Send(async () => await mediator.Send(new AgentCatalogQuery(actor.Require()), cancellationToken));

    [HttpGet("agents/providers")]
    [ProducesResponseType<IReadOnlyList<AgentProviderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Providers(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new AgentProviderListQuery(actor.Require()), cancellationToken));

    /// <summary>Подключить провайдера из каталога или кастомного; 400 — ввод, занятый Id.</summary>
    [HttpPost("agents/providers")]
    [ProducesResponseType<AgentProviderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateAgentProviderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await mediator.Send(new AgentProviderCreateCommand(actor.Require(), request.ProviderKey, request.IsCustom, request.Name,
                request.BaseUrl, request.Models, request.ApiKey), cancellationToken);
            return StatusCode(StatusCodes.Status201Created, provider);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new ApiError(ex.Message));
        }
    }

    /// <summary>PATCH: null — не менять; apiKey — заменить ключ, clearApiKey — удалить.</summary>
    [HttpPatch("agents/providers/{id:guid}")]
    [ProducesResponseType<AgentProviderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Update(Guid id, UpdateAgentProviderRequest request, CancellationToken cancellationToken) =>
        Send(async () => await mediator.Send(new AgentProviderUpdateCommand(actor.Require(), id, request.Name, request.BaseUrl, request.Models,
            request.ApiKey, request.ClearApiKey), cancellationToken));

    [HttpDelete("agents/providers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new AgentProviderDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    /// <summary>Модель по умолчанию; оба поля null — модель из конфигурации хоста.</summary>
    [HttpPut("agents/default-model")]
    [ProducesResponseType<AgentStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> SetDefaultModel(SetAgentDefaultModelRequest request, CancellationToken cancellationToken) =>
        Send(async () => await mediator.Send(new AgentDefaultModelSetCommand(actor.Require(), request.ProviderKey, request.ModelId), cancellationToken));

    /// <summary>Пробный запрос к модели; отказ провайдера — 200 с ok = false и причиной.</summary>
    [HttpPost("agents/check")]
    [ProducesResponseType<AgentCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Check([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AgentCheckRequest? request,
        CancellationToken cancellationToken) =>
        Send(async () => await mediator.Send(new AgentCheckCommand(actor.Require(), request?.ProviderKey, request?.ModelId), cancellationToken));

    /// <summary>Передать провайдеров и ключи в OpenCode заново; ответ — провайдеры с итогом передачи.</summary>
    [HttpPost("agents/apply")]
    [ProducesResponseType<IReadOnlyList<AgentProviderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Apply(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new AgentApplyCommand(actor.Require()), cancellationToken));

    private async Task<IActionResult> Send<T>(Func<Task<T?>> action)
    {
        try
        {
            return await action() is { } result ? Ok(result) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new ApiError(ex.Message));
        }
    }
}
