using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Boards.Commands.BoardDefaultRoleSetCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberRemoveCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Boards.Queries.BoardMembersQuery;
using Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;
using Flow.Application.Features.Boards.Commands.StatusCreateCommand;
using Flow.Application.Features.Boards.Commands.StatusDeleteCommand;
using Flow.Application.Features.Boards.Commands.StatusReorderCommand;
using Flow.Application.Features.Boards.Commands.StatusUpdateCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeCreateCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeUpdateCommand;
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
        Scoped(async mediator => Ok(await mediator.Send(new BoardListQuery(await ActorAsync()), ct)));

    public Task<ApiResult<BoardResponse>> GetBoard(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var board = await mediator.Send(new BoardGetQuery(await ActorAsync(), id), ct);
            return board is null ? NotFound<BoardResponse>() : Ok(board);
        });

    public Task<ApiResult<BoardResponse>> CreateBoard(CreateBoardRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();

            var result = await mediator.Send(new BoardCreateCommand(actor, request.Name, request.Key, request.TemplateId), ct);

            // Занятый ключ — не исключение, а явный результат: экран создания проекта отличает его
            // от невалидного ключа именно по 409 и показывает подсказку рядом с полем «Ключ».
            return result.IsKeyTaken
                ? Conflict<BoardResponse>(result.ValidationError!)
                : Ok(result.Response!);
        });

    public Task<ApiResult<BoardResponse>> RenameBoard(Guid id, RenameBoardRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();

            var response = await mediator.Send(new BoardRenameCommand(actor, id, request.Name), ct);
            return response is null ? NotFound<BoardResponse>() : Ok(response);
        });

    /// <summary>Ответ — проект целиком: вместе с типом мог переехать флаг «по умолчанию». 400 — занятое имя.</summary>
    public Task<ApiResult<BoardResponse>> CreateTaskType(Guid boardId, CreateTaskTypeRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var response = await mediator.Send(
                new TaskTypeCreateCommand(actor, boardId, request.Name, request.Kind.ToDomainKind(), request.IsDefault), ct);
            return response is null ? NotFound<BoardResponse>() : Ok(response);
        });

    public Task<ApiResult<BoardResponse>> UpdateTaskType(Guid boardId, Guid typeId, UpdateTaskTypeRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var response = await mediator.Send(
                new TaskTypeUpdateCommand(actor, boardId, typeId, request.Name, request.IsDefault, request.IsArchived), ct);
            return response is null ? NotFound<BoardResponse>() : Ok(response);
        });

    /// <summary>Статусы (этап 3A): ответ — проект целиком, флаги «начальный»/«финальный» могли переехать.</summary>
    public Task<ApiResult<BoardResponse>> CreateStatus(Guid boardId, CreateStatusRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var response = await mediator.Send(
                new StatusCreateCommand(actor, boardId, request.Name, request.Type?.ToDomainStatusType(), request.IsFinal), ct);
            return response is null ? NotFound<BoardResponse>() : Ok(response);
        });

    public Task<ApiResult<BoardResponse>> UpdateStatus(Guid boardId, Guid statusId, UpdateStatusRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var response = await mediator.Send(
                new StatusUpdateCommand(actor, boardId, statusId, request.Name, request.IsFinal, request.IsInitial,
                    request.Type?.ToDomainStatusType(), request.ClearType, request.WipLimit, request.ClearWipLimit), ct);
            return response is null ? NotFound<BoardResponse>() : Ok(response);
        });

    public Task<ApiResult<BoardResponse>> DeleteStatus(Guid boardId, Guid statusId, Guid moveTo, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var response = await mediator.Send(new StatusDeleteCommand(actor, boardId, statusId, moveTo), ct);
            return response is null ? NotFound<BoardResponse>() : Ok(response);
        });

    public Task<ApiResult<BoardResponse>> ReorderStatuses(Guid boardId, ReorderStatusesRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var response = await mediator.Send(new StatusReorderCommand(actor, boardId, request.StatusIds), ct);
            return response is null ? NotFound<BoardResponse>() : Ok(response);
        });

    public Task<ApiResult<IReadOnlyList<ProjectAccessResponse>>> GetMyAccess(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new BoardMyAccessQuery(await ActorAsync()), ct)));

    public Task<ApiResult<BoardMembersResponse>> GetBoardMembers(Guid boardId, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var members = await mediator.Send(new BoardMembersQuery(await ActorAsync(), boardId), ct);
            return members is null ? NotFound<BoardMembersResponse>() : Ok(members);
        });

    public Task<ApiResult<BoardMembersResponse>> SetBoardMember(Guid boardId, Guid userId, SetBoardMemberRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => MemberResult(await mediator.Send(
            new BoardMemberSetCommand(await ActorAsync(), boardId, userId, request.Role.ToDomainRole(), request.PermissionSetId), ct)));

    public Task<ApiResult<BoardMembersResponse>> RemoveBoardMember(Guid boardId, Guid userId, CancellationToken ct = default) =>
        Scoped(async mediator => MemberResult(await mediator.Send(new BoardMemberRemoveCommand(await ActorAsync(), boardId, userId), ct)));

    public Task<ApiResult<BoardMembersResponse>> SetBoardGroup(Guid boardId, Guid groupId, SetBoardMemberRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => MemberResult(await mediator.Send(
            new Flow.Application.Features.Groups.BoardGroupSetCommand(await ActorAsync(), boardId, groupId, request.Role.ToDomainRole(), request.PermissionSetId), ct)));

    public Task<ApiResult<BoardMembersResponse>> RemoveBoardGroup(Guid boardId, Guid groupId, CancellationToken ct = default) =>
        Scoped(async mediator => MemberResult(await mediator.Send(
            new Flow.Application.Features.Groups.BoardGroupRemoveCommand(await ActorAsync(), boardId, groupId), ct)));

    /// <summary>Admin в роли по умолчанию — ArgumentException, Guard отдаёт 400.</summary>
    public Task<ApiResult<BoardMembersResponse>> SetBoardDefaultRole(Guid boardId, SetDefaultRoleRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var response = await mediator.Send(
                new BoardDefaultRoleSetCommand(await ActorAsync(), boardId, request.Role?.ToDomainRole()), ct);
            return response is null ? NotFound<BoardMembersResponse>() : Ok(response);
        });

    public Task<ApiResult<BoardMembersResponse>> SetBoardVisibility(Guid boardId, SetVisibilityRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var response = await mediator.Send(
                new BoardVisibilitySetCommand(await ActorAsync(), boardId, request.Visibility.ToDomainVisibility()), ct);
            return response is null ? NotFound<BoardMembersResponse>() : Ok(response);
        });

    private static ApiResult<BoardMembersResponse> MemberResult(BoardMemberResult result)
    {
        if (result.IsNotFound)
            return NotFound<BoardMembersResponse>();

        return result.ValidationError is { } error ? Invalid<BoardMembersResponse>(error) : Ok(result.Response!);
    }

    public Task<ApiResult<bool>> DeleteBoard(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();

            // Удаление несуществующей доски — не ошибка для вызывающего: список уже перерисован
            // кем-то другим. Отсюда Missing(), а не NotFound<bool>() — так же вёл себя Delete у HTTP-клиента.
            var deleted = await mediator.Send(new BoardDeleteCommand(actor, id), ct);
            return deleted ? Ok(true) : Missing();
        });
}
