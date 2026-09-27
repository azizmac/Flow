using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;

/// <summary>Бросает ArgumentException при пустом названии (см. TaskItem.Rename).</summary>
internal sealed class TaskUpdateCommandHandler(ITaskItemRepository tasks, IBoardRepository boards, ITaskActivityRepository activities, ISearchIndexQueue searchIndex, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskUpdateCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        if (request.StatusId is not null)
        {
            var statusBelongsToBoard = await tasks.StatusBelongsToBoardAsync(request.StatusId.Value, task.BoardId, cancellationToken);
            if (!statusBelongsToBoard)
                return TaskUpdateResult.InvalidStatus(request.StatusId.Value);
        }

        // Тип живёт в агрегате Board — доску грузим, только когда тип действительно меняют.
        // Новый уровень обязан остаться между родителем и детьми (docs/TZ_task_model.md §3).
        TaskType? newType = null;
        int? parentLevel = null;
        int? minChildLevel = null;
        if (request.TypeId is { } typeId && typeId != task.TypeId)
        {
            var board = await boards.GetByIdAsync(task.BoardId, cancellationToken);
            newType = board?.TaskTypes.SingleOrDefault(t => t.Id == typeId);
            if (newType is null)
                return TaskUpdateResult.InvalidType(typeId);

            if (task.ParentId is { } parentId && await tasks.GetByIdAsync(parentId, cancellationToken) is { } parent)
                parentLevel = board!.GetTaskType(parent.TypeId).Level;

            var children = await tasks.GetChildrenAsync(task.Id, cancellationToken);
            if (children.Count > 0)
                minChildLevel = children.Min(c => board!.GetTaskType(c.TypeId).Level);
        }

        // Журнал: по записи на каждое реально изменённое поле; то же значение — без записи.
        var textChanged = false;

        if (request.Title is not null)
        {
            var oldTitle = task.Title;
            task.Rename(request.Title);
            if (task.Title != oldTitle)
            {
                activities.Add(TaskActivity.TitleChanged(task.Id, actor.Id, oldTitle, task.Title));
                textChanged = true;
            }
        }

        if (request.Description is not null && request.Description != (task.Description ?? string.Empty))
        {
            task.UpdateDescription(request.Description);
            activities.Add(TaskActivity.DescriptionChanged(task.Id, actor.Id));
            textChanged = true;
        }

        var statusChanged = false;
        if (request.StatusId is not null && request.StatusId.Value != task.StatusId)
        {
            activities.Add(TaskActivity.StatusChanged(task.Id, actor.Id, task.StatusId, request.StatusId.Value));
            task.ChangeStatus(request.StatusId.Value);
            statusChanged = true;
        }

        if (newType is not null)
        {
            var oldTypeId = task.TypeId;
            task.ChangeType(newType, parentLevel, minChildLevel);
            activities.Add(TaskActivity.TypeChanged(task.Id, actor.Id, oldTypeId, newType.Id));
        }

        if (request.Priority is { } priority && priority != task.Priority)
        {
            var oldPriority = task.Priority;
            task.SetPriority(priority);
            activities.Add(TaskActivity.PriorityChanged(task.Id, actor.Id, oldPriority, priority));
        }

        // Тип и приоритет в текст чанков не входят — индекс они не трогают, как исполнитель и срок.
        // Смена статуса тоже идёт в очередь, но реэмбеддинга не вызывает: текст чанков не изменился,
        // воркер увидит тот же ContentHash и обновит только флаг IsClosed.
        if (textChanged || statusChanged)
            searchIndex.Enqueue(SearchSourceType.Task, task.Id, task.BoardId, SearchIndexOperation.Upsert);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskUpdateResult.Success(task.ToResponse());
    }
}
