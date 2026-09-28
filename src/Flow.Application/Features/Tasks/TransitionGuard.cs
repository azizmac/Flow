using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;

namespace Flow.Application.Features.Tasks;

/// <summary>
/// Одна проверка перехода для всех путей смены статуса (docs/TZ_workflow_config.md §2): собирает то, чего домен
/// сам не знает, — роль actor'а в проекте, закрыты ли подзадачи, выполнен ли чек-лист — и спрашивает граф
/// проекта — по workflow типа задачи, свой он у типа или общий (<see cref="Board.CheckTransition"/>, этап 3E).
/// Служебный перенос при удалении статуса её не проходит намеренно.
/// </summary>
internal sealed class TransitionGuard(ITaskItemRepository tasks)
{
    public async Task<TransitionContext> ContextAsync(ProjectAccessInfo access, Board board, TaskItem task, CancellationToken cancellationToken)
    {
        // Подзадачи — только если это вообще нужно: в Free граф не проверяется, а без детей всё «закрыто».
        var childrenDone = true;
        if (board.WorkflowModeFor(task.TypeId) == WorkflowMode.Restricted && board.TransitionsFor(task.TypeId).Any(t => t.RequireChildrenDone))
        {
            var finals = board.Statuses.Where(s => s.IsFinal).Select(s => s.Id).ToHashSet();
            childrenDone = (await tasks.GetChildrenAsync(task.Id, cancellationToken)).All(c => finals.Contains(c.StatusId));
        }

        return new TransitionContext(
            access.Role ?? ProjectRole.Viewer,
            task.AssigneeId is not null,
            childrenDone,
            task.ChecklistDone == task.ChecklistTotal,
            task.CustomFieldValues().Keys.ToHashSet());
    }

    public async Task<TransitionCheck> CheckAsync(ProjectAccessInfo access, Board board, TaskItem task, Guid toStatusId, CancellationToken cancellationToken) =>
        board.CheckTransition(task.StatusId, toStatusId, await ContextAsync(access, board, task, cancellationToken), task.TypeId);
}
