using Flow.Application.Abstractions;
using Flow.Application.Features.Scm;
using Flow.Shared.Contracts.Scm;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Интеграция с Git-хостингами (docs/TZ_scm_integration.md): подключения и репозитории — глобальные Admin+;
/// привязка к проекту — ManageScm; блок «Разработка» задачи — любой, кто видит проект. Отказ хостинга — 400 с причиной.
/// </summary>
[ApiController]
public class ScmController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet("scm/connections")]
    public async Task<IActionResult> Connections(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ScmConnectionListQuery(actor.Require()), cancellationToken));

    [HttpPost("scm/connections")]
    public Task<IActionResult> Create(CreateScmConnectionRequest request, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new ScmConnectionCreateCommand(actor.Require(), request.Provider, request.Name, request.Token, request.BaseUrl,
            request.AuthKind, request.AppId, request.InstallationId), cancellationToken));

    [HttpPatch("scm/connections/{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateScmConnectionRequest request, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new ScmConnectionUpdateCommand(actor.Require(), id, request.Name, request.BaseUrl, request.Token,
            request.AppId, request.InstallationId), cancellationToken));

    [HttpDelete("scm/connections/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new ScmConnectionDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    [HttpPost("scm/connections/{id:guid}/check")]
    public Task<IActionResult> Check(Guid id, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new ScmConnectionCheckCommand(actor.Require(), id), cancellationToken));

    [HttpGet("scm/connections/{id:guid}/available-repositories")]
    public Task<IActionResult> Available(Guid id, [FromQuery] string? q, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new ScmAvailableRepositoriesQuery(actor.Require(), id, q), cancellationToken));

    /// <summary>Создаёт вебхук; не вышло — 201 с адресом и секретом для ручной настройки (только в этом ответе).</summary>
    [HttpPost("scm/repositories")]
    public async Task<IActionResult> AddRepository(AddScmRepositoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var repository = await mediator.Send(new ScmRepositoryAddCommand(actor.Require(), request.ConnectionId, request.ExternalId), cancellationToken);
            return repository is null ? NotFound() : StatusCode(StatusCodes.Status201Created, repository);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Отключить: вебхук удаляется у хостинга, связи задач остаются.</summary>
    [HttpDelete("scm/repositories/{id:guid}")]
    public async Task<IActionResult> DisableRepository(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new ScmRepositoryDisableCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    [HttpGet("scm/repositories/{id:guid}/deliveries")]
    public async Task<IActionResult> Deliveries(Guid id, [FromQuery] ScmDeliveryStatus? status, CancellationToken cancellationToken) =>
        await mediator.Send(new ScmDeliveriesQuery(actor.Require(), id, status), cancellationToken) is { } list ? Ok(list) : NotFound();

    /// <summary>Повторить доставку с ошибкой; не Failed — 400.</summary>
    [HttpPost("scm/deliveries/{id:guid}/retry")]
    public Task<IActionResult> RetryDelivery(Guid id, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new ScmDeliveryRetryCommand(actor.Require(), id), cancellationToken));

    /// <summary>Дозагрузить историю (последние PR и коммиты ветки по умолчанию) — фоном, 202; отключённый — 400.</summary>
    [HttpPost("scm/repositories/{id:guid}/backfill")]
    public async Task<IActionResult> Backfill(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(new ScmBackfillCommand(actor.Require(), id), cancellationToken) ? Accepted() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpGet("boards/{boardId:guid}/repositories")]
    public async Task<IActionResult> BoardRepositories(Guid boardId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ScmBoardRepositoriesQuery(actor.Require(), boardId), cancellationToken));

    /// <summary>Привязать; тело (автопереходы, смарт-коммиты) необязательно — без него настройки не меняются.</summary>
    [HttpPut("boards/{boardId:guid}/repositories/{repositoryId:guid}")]
    public Task<IActionResult> Bind(Guid boardId, Guid repositoryId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] UpdateScmBindingRequest? request,
        CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new ScmBindCommand(actor.Require(), boardId, repositoryId, true, request), cancellationToken));

    [HttpDelete("boards/{boardId:guid}/repositories/{repositoryId:guid}")]
    public Task<IActionResult> Unbind(Guid boardId, Guid repositoryId, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new ScmBindCommand(actor.Require(), boardId, repositoryId, false), cancellationToken));

    [HttpGet("tasks/{id:guid}/development")]
    public async Task<IActionResult> Development(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new TaskDevelopmentQuery(actor.Require(), id), cancellationToken) is { } development ? Ok(development) : NotFound();

    private async Task<IActionResult> Send(Func<Task<object?>> action)
    {
        try
        {
            return await action() is { } result ? Ok(result) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
