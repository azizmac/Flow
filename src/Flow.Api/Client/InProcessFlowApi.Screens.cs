using Flow.Application.Features.Boards.Commands.ScreenSetCommand;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;
using DomainContext = Flow.Domain.Entities.ScreenContext;

namespace Flow.Api.Client;

/// <summary>Экраны задач (docs/TZ_workflow_config.md §3) — коды как у ScreensController; ошибки ввода ловит Guard (400).</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<BoardResponse>> SetScreen(Guid boardId, Guid? taskTypeId, ScreenContext context, SetScreenRequest request, CancellationToken ct = default) =>
        SendBoard(actor => new ScreenSetCommand(actor, boardId, taskTypeId, (DomainContext)(int)context, request.Fields), ct);

    public Task<ApiResult<BoardResponse>> ResetScreen(Guid boardId, Guid? taskTypeId, ScreenContext context, CancellationToken ct = default) =>
        SendBoard(actor => new ScreenResetCommand(actor, boardId, taskTypeId, (DomainContext)(int)context), ct);
}
