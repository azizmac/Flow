using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Вехи проектов (docs/TZ_task_views.md §6).</summary>
public interface IMilestoneRepository
{
    /// <summary>Веха, отслеживаемая.</summary>
    Task<Milestone?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Вехи проекта — свои и общие с ним (этап 2H): открытые (свои первыми) по SortOrder, затем закрытые от недавних.</summary>
    Task<IReadOnlyList<Milestone>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken);

    Task<int> NextSortOrderAsync(Guid boardId, CancellationToken cancellationToken);

    void Add(Milestone milestone);

    void Remove(Milestone milestone);
}

/// <summary>Счётчики прогресса одной вехи — сырьё для MilestoneProgress (прогноз считает Application).</summary>
public readonly record struct MilestoneCounts(int Total, int Done, int InProgress, decimal Points, decimal DonePoints, int Overdue, int ClosedRecently);
