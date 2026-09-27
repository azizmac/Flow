using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
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

        var task = board.CreateTask(request.Title, request.Description, request.StatusId, createdById: actor.Id, typeId: request.TypeId);

        // Приоритет в журнал отдельно не пишется: запись Created и так фиксирует начальное состояние задачи.
        if (request.Priority is { } priority)
            task.SetPriority(priority);

        // Board.Tasks не подгружен (не нужен для создания), поэтому EF не отследит новую задачу
        // через изменение коллекции сам — регистрируем её явно.
        tasks.Add(task);
        activities.Add(TaskActivity.Created(task.Id, actor.Id));
        searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Upsert);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return task.ToResponse();
    }
}
