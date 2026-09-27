using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Features.Milestones;

/// <summary>
/// Смена вехи задачи — один путь для поля в карточке и удаления вехи: веха того же проекта, не закрытая (если задача
/// в ней ещё не была), запись MilestoneChanged в журнале. Индекс поиска не трогается — текст задачи тот же.
/// </summary>
internal sealed class TaskMilestones(IMilestoneRepository milestones, ITaskActivityRepository activities)
{
    /// <summary>true — веха поменялась; вехи нет или она чужая — InvalidOperationException (400).</summary>
    public async Task<bool> MoveAsync(TaskItem task, Guid? milestoneId, Guid actorId, CancellationToken cancellationToken)
    {
        if (task.MilestoneId == milestoneId)
            return false;

        Milestone? milestone = null;
        if (milestoneId is { } id)
        {
            milestone = await milestones.GetByIdAsync(id, cancellationToken);
            if (milestone is null || milestone.BoardId != task.BoardId)
                throw new InvalidOperationException($"Milestone {id} is not found in the task's project.");
        }

        var old = task.MilestoneId;
        task.SetMilestone(milestone);
        activities.Add(TaskActivity.MilestoneChanged(task.Id, actorId, old, milestoneId));
        return true;
    }
}
