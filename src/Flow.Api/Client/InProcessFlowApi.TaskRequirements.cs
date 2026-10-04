using System.Net;
using Flow.Application.Features.Agents.Commands.TaskRequirementsReviewCommand;
using Flow.Client.Services;
using Flow.Shared.Contracts.Agents;

namespace Flow.Api.Client;

internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<TaskRequirementsResponse>> ReviewTaskRequirements(
        TaskRequirementsRequest request,
        CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            try
            {
                return Ok(await mediator.Send(new TaskRequirementsReviewCommand(
                    await ActorAsync(), request.BoardId, request.Title, request.Description, request.TaskId), ct));
            }
            catch (HttpRequestException)
            {
                return ApiResult<TaskRequirementsResponse>.Fail(
                    "Не удалось получить рекомендации от OpenCode. Проверьте доступность агента и локальной модели.",
                    HttpStatusCode.BadGateway);
            }
            catch (Exception ex) when (ex is TimeoutException || ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                return ApiResult<TaskRequirementsResponse>.Fail(
                    "Агент не завершил анализ за отведённое время. Попробуйте повторить запрос.",
                    HttpStatusCode.BadGateway);
            }
        });
}
