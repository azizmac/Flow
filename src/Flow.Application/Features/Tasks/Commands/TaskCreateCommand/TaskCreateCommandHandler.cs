using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Ranking;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCreateCommand;

/// <summary>Бросает ArgumentException/InvalidOperationException при невалидных данных (см. Board.CreateTask).</summary>
internal sealed class TaskCreateCommandHandler(IBoardRepository boards, ITaskItemRepository tasks, ITaskActivityRepository activities, ISearchIndexQueue searchIndex, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskCreateCommand, TaskResponse?>
{
    public async Task<TaskResponse?> Handle(TaskCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanCreateTask(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        // Новая задача встаёт в конец ручного порядка проекта (docs/TZ_task_model.md §7).
        var rank = FractionalIndex.Between(await tasks.GetMaxRankAsync(board.Id, null, cancellationToken), null);
        var task = board.CreateTask(request.Title, request.Description, request.StatusId, createdById: actor.Id, typeId: request.TypeId, rank: rank);

        // Родитель проверяется доменом: тот же проект, строго выше по уровню. Скрытая или чужая задача — «не найдена».
        TaskItem? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await tasks.GetByIdAsync(parentId, cancellationToken);
            if (parent is null || parent.BoardId != board.Id)
                throw new InvalidOperationException($"Parent task {parentId} is not found in project {board.Key}.");

            task.SetParent(parent, board.GetTaskType(task.TypeId), board.GetTaskType(parent.TypeId));
        }

        // Приоритет в журнал отдельно не пишется: запись Created и так фиксирует начальное состояние задачи.
        if (request.Priority is { } priority)
            task.SetPriority(priority);

        // Board.Tasks не подгружен (не нужен для создания), поэтому EF не отследит новую задачу
        // через изменение коллекции сам — регистрируем её явно.
        tasks.Add(task);
        activities.Add(TaskActivity.Created(task.Id, actor.Id));
        if (parent is not null)
            activities.Add(TaskActivity.ChildAdded(parent.Id, actor.Id, task.Id));
        searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Upsert);

        await TaskRanks.SaveAsync(unitOfWork, async () =>
            task.SetRank(FractionalIndex.Between(await tasks.GetMaxRankAsync(board.Id, task.Id, cancellationToken), null)), cancellationToken);

        return task.ToResponse();
    }
}
