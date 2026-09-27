using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Application.Features.Tasks;

/// <summary>
/// Ответы для чтения задач со всеми счётчиками — комментарии, подзадачи, блокировки — по одному GROUP BY на каждый,
/// сколько бы задач ни было (не N+1). Один сборщик на все запросы: новый счётчик не забудется ни в одном из них.
/// В ответах команд счётчиков нет намеренно (0) — клиент берёт их из того, что уже показывает.
/// </summary>
internal sealed class TaskResponses(ITaskItemRepository tasks, ITaskCommentRepository comments, ITaskLinkRepository links)
{
    public async Task<IReadOnlyList<TaskResponse>> BuildAsync(IReadOnlyList<TaskItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return [];

        var ids = items.Select(t => t.Id).ToList();
        var commentCounts = await comments.CountByTaskIdsAsync(ids, cancellationToken);
        var children = await tasks.CountChildrenAsync(ids, cancellationToken);
        var blockers = await links.CountBlockersAsync(ids, cancellationToken);

        return items
            .Select(t => t.ToResponse(commentCounts.GetValueOrDefault(t.Id), children.GetValueOrDefault(t.Id), blockers.GetValueOrDefault(t.Id)))
            .ToList();
    }

    public async Task<TaskResponse> BuildAsync(TaskItem item, CancellationToken cancellationToken) =>
        (await BuildAsync([item], cancellationToken))[0];
}
