using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentEditCommand;
using Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;
using Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Client;

/// <summary>
/// Лента задачи — зеркало CommentsController. Отличий в кодах нет: список и журнал на null отдают 404,
/// добавление и правка — 404 по IsNotFound, удаление — 404 как штатный «уже нет» (см. Missing).
/// ArgumentException из домена (пустое тело) и отказ PermissionService ловит Guard, как это делали
/// catch в контроллере и ApiExceptionFilter.
/// </summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<TaskCommentResponse>>> GetComments(Guid taskId, CancellationToken ct = default) =>
        Guard(async () =>
        {
            // Actor не нужен: читать ленту могут все роли, запрос его и не принимает.
            var comments = await mediator.Send(new TaskCommentListQuery(taskId), ct);
            return comments is null ? NotFound<IReadOnlyList<TaskCommentResponse>>() : Ok(comments);
        });

    public Task<ApiResult<TaskCommentResponse>> AddComment(Guid taskId, CreateTaskCommentRequest request, CancellationToken ct = default) =>
        Guard(async () =>
        {
            // ActorAsync внутри Guard: без сессии экран должен получить 401, а не исключение в рендере.
            var actor = await ActorAsync();
            var result = await mediator.Send(new TaskCommentAddCommand(actor, taskId, request.Body), ct);

            // Response непустой всегда, когда не IsNotFound, — это инвариант TaskCommentResult.
            return result.IsNotFound ? NotFound<TaskCommentResponse>() : Ok(result.Response!);
        });

    public Task<ApiResult<TaskCommentResponse>> UpdateComment(Guid id, UpdateTaskCommentRequest request, CancellationToken ct = default) =>
        Guard(async () =>
        {
            var actor = await ActorAsync();
            var result = await mediator.Send(new TaskCommentEditCommand(actor, id, request.Body), ct);
            return result.IsNotFound ? NotFound<TaskCommentResponse>() : Ok(result.Response!);
        });

    public Task<ApiResult<bool>> DeleteComment(Guid id, CancellationToken ct = default) =>
        Guard(async () =>
        {
            var actor = await ActorAsync();
            var deleted = await mediator.Send(new TaskCommentDeleteCommand(actor, id), ct);

            // Не найден — не ошибка: у HTTP-клиента DELETE на 404 отдавал Success(false), и лента
            // по нему просто убирает комментарий, который кто-то удалил раньше нас.
            return deleted ? Ok(true) : Missing();
        });

    public Task<ApiResult<IReadOnlyList<TaskActivityResponse>>> GetActivity(Guid taskId, CancellationToken ct = default) =>
        Guard(async () =>
        {
            var activity = await mediator.Send(new TaskActivityListQuery(taskId), ct);
            return activity is null ? NotFound<IReadOnlyList<TaskActivityResponse>>() : Ok(activity);
        });
}
