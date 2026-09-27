using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

/// <summary>Вехи в памяти. Удаление повторяет FK SetNull из БД: задачи удалённой вехи остаются без вехи.</summary>
public sealed class FakeMilestoneRepository(FakeTaskItemRepository tasks) : IMilestoneRepository
{
    private readonly List<Milestone> _milestones = [];

    public Task<Milestone?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_milestones.FirstOrDefault(m => m.Id == id));

    public Task<IReadOnlyList<Milestone>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Milestone>>(_milestones
            .Where(m => m.BoardId == boardId)
            .OrderBy(m => m.IsClosed)
            .ThenBy(m => m.IsClosed ? 0 : m.SortOrder)
            .ThenByDescending(m => m.ClosedAt)
            .ToList());

    public Task<int> NextSortOrderAsync(Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult(_milestones.Where(m => m.BoardId == boardId).Select(m => m.SortOrder).DefaultIfEmpty(-1).Max() + 1);

    public void Add(Milestone milestone) => _milestones.Add(milestone);

    public void Remove(Milestone milestone)
    {
        _milestones.Remove(milestone);
        foreach (var task in tasks.GetByMilestoneIdAsync(milestone.Id, CancellationToken.None).Result)
            task.SetMilestone(null);
    }
}
