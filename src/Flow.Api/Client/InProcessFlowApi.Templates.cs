using Flow.Application.Features.Templates;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;

namespace Flow.Api.Client;

/// <summary>Шаблоны проектов и перенос конфигурации (этап 3F) — как BoardTemplatesController.</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<BoardTemplateResponse>>> GetBoardTemplates(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new BoardTemplateListQuery(await ActorAsync()), ct)));

    public Task<ApiResult<BoardTemplateResponse>> SaveBoardAsTemplate(Guid boardId, SaveBoardTemplateRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var template = await mediator.Send(new BoardTemplateSaveCommand(await ActorAsync(), boardId, request.Name, request.Description, request.IncludeTasks), ct);
            return template is null ? NotFound<BoardTemplateResponse>() : Ok(template);
        });

    public Task<ApiResult<bool>> DeleteBoardTemplate(Guid templateId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new BoardTemplateDeleteCommand(await ActorAsync(), templateId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<ApplyConfigPreviewResponse>> PreviewApplyConfig(Guid sourceBoardId, ApplyBoardConfigRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var preview = await mediator.Send(new BoardApplyConfigPreviewQuery(await ActorAsync(), sourceBoardId, request.Targets, request.Parts), ct);
            return preview is null ? NotFound<ApplyConfigPreviewResponse>() : Ok(preview);
        });

    public Task<ApiResult<ApplyBoardConfigResponse>> ApplyConfig(Guid sourceBoardId, ApplyBoardConfigRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var result = await mediator.Send(new BoardApplyConfigCommand(await ActorAsync(), sourceBoardId, request.Targets, request.Parts), ct);
            return result is null ? NotFound<ApplyBoardConfigResponse>() : Ok(result);
        });
}
