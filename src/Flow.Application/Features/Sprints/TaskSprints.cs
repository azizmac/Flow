using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Features.Sprints;

/// <summary>
/// Перенос задачи в спринт или бэклог — один путь для поля в карточке и перетаскивания в бэклоге
/// (docs/TZ_task_views.md §2): спринт того же проекта, не завершённый, запись SprintChanged в журнале.
/// Индекс поиска не трогается — текст задачи тот же, как у исполнителя.
/// </summary>
internal sealed class TaskSprints(ISprintRepository sprints, ITaskActivityRepository activities)
{
    /// <summary>true — спринт поменялся; спринта нет или он чужой — InvalidOperationException (400).</summary>
    public async Task<bool> MoveAsync(TaskItem task, Guid? sprintId, Guid actorId, CancellationToken cancellationToken)
    {
        if (task.SprintId == sprintId)
            return false;

        Sprint? sprint = null;
        if (sprintId is { } id)
        {
            sprint = await sprints.GetByIdAsync(id, cancellationToken);
            if (sprint is null || sprint.BoardId != task.BoardId)
                throw new InvalidOperationException($"Sprint {id} is not found in the task's project.");
        }

        var old = task.SprintId;
        task.SetSprint(sprint);
        activities.Add(TaskActivity.SprintChanged(task.Id, actorId, old, sprintId));
        return true;
    }
}
