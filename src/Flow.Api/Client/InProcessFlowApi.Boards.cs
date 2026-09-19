using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;

namespace Flow.Api.Client;

/// <summary>
/// Проекты: тот же набор кодов, что отдавал BoardsController — 404 на отсутствующую доску,
/// 409 на занятый ключ, 400 на невалидные Name/Key (ArgumentException из Board.Create ловит Guard),
/// 403 на нехватку прав (EnsureCanManageBoards).
/// </summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<BoardResponse>>> GetBoards(CancellationToken ct = default) =>
        Guard(async () => Ok(await mediator.Send(new BoardListQuery(), ct)));

    public Task<ApiResult<BoardResponse>> GetBoard(Guid id, CancellationToken ct = default) =>
        Guard(async () =>
        {
            var board = await mediator.Send(new BoardGetQuery(id), ct);
            return board is null ? NotFound<BoardResponse>() : Ok(board);
        });

    public Task<ApiResult<BoardResponse>> CreateBoard(CreateBoardRequest request, CancellationToken ct = default) =>
        Guard(async () =>
        {
            var actor = await ActorAsync();

            var result = await mediator.Send(new BoardCreateCommand(actor, request.Name, request.Key), ct);

            // Занятый ключ — не исключение, а явный результат: экран создания проекта отличает его
            // от невалидного ключа именно по 409 и показывает подсказку рядом с полем «Ключ».
            return result.IsKeyTaken
                ? Conflict<BoardResponse>(result.ValidationError!)
                : Ok(result.Response!);
        });

    public Task<ApiResult<BoardResponse>> RenameBoard(Guid id, RenameBoardRequest request, CancellationToken ct = default) =>
        Guard(async () =>
        {
            var actor = await ActorAsync();

            var response = await mediator.Send(new BoardRenameCommand(actor, id, request.Name), ct);
            return response is null ? NotFound<BoardResponse>() : Ok(response);
        });

    public Task<ApiResult<bool>> DeleteBoard(Guid id, CancellationToken ct = default) =>
        Guard(async () =>
        {
            var actor = await ActorAsync();

            // Удаление несуществующей доски — не ошибка для вызывающего: список уже перерисован
            // кем-то другим. Отсюда Missing(), а не NotFound<bool>() — так же вёл себя Delete у HTTP-клиента.
            var deleted = await mediator.Send(new BoardDeleteCommand(actor, id), ct);
            return deleted ? Ok(true) : Missing();
        });
}
