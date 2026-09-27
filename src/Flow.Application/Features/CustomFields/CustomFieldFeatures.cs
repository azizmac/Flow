using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Tasks;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.CustomFields;

/// <summary>Новое поле проекта (docs/TZ_task_model.md §4). Права — ManageConfig; ответ — проект целиком, как у типов задач.</summary>
public sealed record CustomFieldCreateCommand(
    Guid ActorId,
    Guid BoardId,
    string Key,
    string Name,
    CustomFieldType Type,
    IReadOnlyList<string>? Options = null,
    bool IsRequired = false,
    IReadOnlyList<Guid>? TaskTypeIds = null) : IRequest<BoardResponse?>;

/// <summary>PATCH поля: null — не трогать; ключ и тип не меняются. Архив — IsArchived. Права — ManageConfig.</summary>
public sealed record CustomFieldUpdateCommand(
    Guid ActorId,
    Guid BoardId,
    Guid FieldId,
    string? Name = null,
    IReadOnlyList<(Guid? Id, string Label, string? Color)>? Options = null,
    bool? IsRequired = null,
    IReadOnlyList<Guid>? TaskTypeIds = null,
    bool? IsArchived = null) : IRequest<BoardResponse?>;

/// <summary>Полная перестановка полей проекта. Права — ManageConfig.</summary>
public sealed record CustomFieldReorderCommand(Guid ActorId, Guid BoardId, IReadOnlyList<Guid> FieldIds) : IRequest<BoardResponse?>;

/// <summary>
/// Значения полей задачи, PATCH-семантика по полям (null очищает). Это правка задачи (EnsureCanEditTask); журнал
/// CustomFieldChanged по каждому изменённому полю, Upsert в поиске — если поменялся текст.
/// </summary>
public sealed record TaskSetCustomFieldsCommand(Guid ActorId, Guid TaskId, IReadOnlyDictionary<Guid, JsonElement?> Values)
    : IRequest<TaskUpdateResult>;

internal sealed class CustomFieldConfigHandlers(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CustomFieldCreateCommand, BoardResponse?>,
      IRequestHandler<CustomFieldUpdateCommand, BoardResponse?>,
      IRequestHandler<CustomFieldReorderCommand, BoardResponse?>
{
    public Task<BoardResponse?> Handle(CustomFieldCreateCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.ActorId, request.BoardId, board =>
            board.AddCustomField(request.Key, request.Name, request.Type, request.Options, request.IsRequired, request.TaskTypeIds), cancellationToken);

    public Task<BoardResponse?> Handle(CustomFieldUpdateCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.ActorId, request.BoardId, board =>
            board.UpdateCustomField(request.FieldId, request.Name, request.Options, request.IsRequired, request.TaskTypeIds, request.IsArchived), cancellationToken);

    public Task<BoardResponse?> Handle(CustomFieldReorderCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.ActorId, request.BoardId, board => board.ReorderCustomFields(request.FieldIds), cancellationToken);

    private async Task<BoardResponse?> ChangeAsync(Guid actorId, Guid boardId, Action<Board> change, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(actorId, cancellationToken);
        permissions.EnsureCanManageConfig(await projectAccess.GetAsync(actor, boardId, cancellationToken));

        var board = await boards.GetByIdAsync(boardId, cancellationToken);
        if (board is null)
            return null;

        change(board);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}

internal sealed class TaskSetCustomFieldsCommandHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    TaskCustomFields customFields,
    ISearchIndexQueue searchIndex,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<TaskSetCustomFieldsCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskSetCustomFieldsCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);
        var board = await boards.GetByIdAsync(task.BoardId, cancellationToken)
            ?? throw new InvalidOperationException($"Project of task {task.Id} is not found.");

        if (await customFields.ApplyAsync(board, task, request.Values, actor.Id, journal: true, cancellationToken))
            searchIndex.Enqueue(SearchSourceType.Task, task.Id, task.BoardId, SearchIndexOperation.Upsert);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TaskUpdateResult.Success(task.ToResponse());
    }
}
