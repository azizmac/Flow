using Flow.Application.Abstractions;
using Flow.Application.Features.GitIntegration;
using Flow.Shared.Contracts.GitIntegration;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Интеграция с Git-хостингами (docs/TZ_git_integration.md): подключения и репозитории — глобальные Admin+;
/// привязка к проекту — ManageGit; блок «Разработка» задачи — любой, кто видит проект. Отказ хостинга — 400 с причиной.
/// </summary>
[ApiController]
public class GitController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet("git/connections")]
    public async Task<IActionResult> Connections(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GitHostConnectionListQuery(actor.Require()), cancellationToken));

    [HttpPost("git/connections")]
    public Task<IActionResult> Create(CreateGitHostConnectionRequest request, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new GitHostConnectionCreateCommand(actor.Require(), request.Provider, request.Name, request.Token, request.BaseUrl,
            request.AuthKind, request.AppId, request.InstallationId), cancellationToken));

    [HttpPatch("git/connections/{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateGitHostConnectionRequest request, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new GitHostConnectionUpdateCommand(actor.Require(), id, request.Name, request.BaseUrl, request.Token,
            request.AppId, request.InstallationId), cancellationToken));

    [HttpDelete("git/connections/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new GitHostConnectionDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    [HttpPost("git/connections/{id:guid}/check")]
    public Task<IActionResult> Check(Guid id, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new GitHostConnectionCheckCommand(actor.Require(), id), cancellationToken));

    [HttpGet("git/connections/{id:guid}/available-repositories")]
    public Task<IActionResult> Available(Guid id, [FromQuery] string? q, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new GitAvailableRepositoriesQuery(actor.Require(), id, q), cancellationToken));

    /// <summary>Создаёт вебхук; не вышло — 201 с адресом и секретом для ручной настройки (только в этом ответе).</summary>
    [HttpPost("git/repositories")]
    public async Task<IActionResult> AddRepository(AddGitRepositoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var repository = await mediator.Send(new GitRepositoryAddCommand(actor.Require(), request.ConnectionId, request.ExternalId), cancellationToken);
            return repository is null ? NotFound() : StatusCode(StatusCodes.Status201Created, repository);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Отключить: вебхук удаляется у хостинга, связи задач остаются.</summary>
    [HttpDelete("git/repositories/{id:guid}")]
    public async Task<IActionResult> DisableRepository(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new GitRepositoryDisableCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    [HttpGet("git/repositories/{id:guid}/deliveries")]
    public async Task<IActionResult> Deliveries(Guid id, [FromQuery] GitIntegrationJobStatus? status, CancellationToken cancellationToken) =>
        await mediator.Send(new GitIntegrationJobsQuery(actor.Require(), id, status), cancellationToken) is { } list ? Ok(list) : NotFound();

    /// <summary>Повторить доставку с ошибкой; не Failed — 400.</summary>
    [HttpPost("git/deliveries/{id:guid}/retry")]
    public Task<IActionResult> RetryDelivery(Guid id, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new GitIntegrationJobRetryCommand(actor.Require(), id), cancellationToken));

    /// <summary>Дозагрузить историю (последние PR и коммиты ветки по умолчанию) — фоном, 202; отключённый — 400.</summary>
    [HttpPost("git/repositories/{id:guid}/backfill")]
    public async Task<IActionResult> Backfill(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(new GitBackfillCommand(actor.Require(), id), cancellationToken) ? Accepted() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpGet("boards/{boardId:guid}/repositories")]
    public async Task<IActionResult> BoardRepositories(Guid boardId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GitBoardRepositoriesQuery(actor.Require(), boardId), cancellationToken));

    /// <summary>Привязать; тело (автопереходы, смарт-коммиты) необязательно — без него настройки не меняются.</summary>
    [HttpPut("boards/{boardId:guid}/repositories/{repositoryId:guid}")]
    public Task<IActionResult> Bind(Guid boardId, Guid repositoryId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] UpdateGitBindingRequest? request,
        CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new GitBindCommand(actor.Require(), boardId, repositoryId, true, request), cancellationToken));

    [HttpDelete("boards/{boardId:guid}/repositories/{repositoryId:guid}")]
    public Task<IActionResult> Unbind(Guid boardId, Guid repositoryId, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new GitBindCommand(actor.Require(), boardId, repositoryId, false), cancellationToken));

    [HttpGet("tasks/{id:guid}/development")]
    public async Task<IActionResult> Development(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new TaskDevelopmentQuery(actor.Require(), id), cancellationToken) is { } development ? Ok(development) : NotFound();

    /// <summary>Этап 5D: ветка задачи на хостинге — WriteGit, репозиторий привязан к проекту; отказ хостинга — 400.</summary>
    [HttpPost("tasks/{id:guid}/development/branch")]
    public Task<IActionResult> CreateBranch(Guid id, CreateGitBranchRequest request, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new TaskGitBranchCreateCommand(actor.Require(), id, request.RepositoryId, request.Name, request.FromBranch), cancellationToken));

    [HttpPost("tasks/{id:guid}/development/pull-request")]
    public Task<IActionResult> CreatePullRequest(Guid id, CreateGitPullRequestRequest request, CancellationToken cancellationToken) =>
        Send(async () => (object?)await mediator.Send(new TaskGitPullRequestCreateCommand(actor.Require(), id, request.RepositoryId, request.SourceBranch,
            request.TargetBranch, request.Title, request.Draft), cancellationToken));

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
