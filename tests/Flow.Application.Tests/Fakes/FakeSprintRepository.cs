using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Спринты в памяти. Удаление повторяет FK SetNull из БД: задачи удалённого спринта возвращаются в бэклог.
/// </summary>
public sealed class FakeSprintRepository(FakeTaskItemRepository tasks) : ISprintRepository
{
    private readonly List<Sprint> _sprints = [];

    public Task<Sprint?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_sprints.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<Sprint>> GetByBoardAsync(Guid boardId, bool includeCompleted, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Sprint>>(_sprints
            .Where(s => s.BoardId == boardId && (includeCompleted || !s.IsCompleted))
            .OrderBy(s => s.State switch { SprintState.Active => 0, SprintState.Planned => 1, _ => 2 })
            .ThenBy(s => s.IsCompleted ? 0 : s.SortOrder)
            .ThenByDescending(s => s.CompletedAt)
            .ToList());

    public Task<bool> HasActiveAsync(Guid boardId, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(_sprints.Any(s => s.BoardId == boardId && s.State == SprintState.Active && s.Id != exceptId));

    public Task<int> NextSortOrderAsync(Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult(_sprints.Where(s => s.BoardId == boardId).Select(s => s.SortOrder).DefaultIfEmpty(-1).Max() + 1);

    public void Add(Sprint sprint) => _sprints.Add(sprint);

    public void Remove(Sprint sprint)
    {
        _sprints.Remove(sprint);
        foreach (var task in tasks.GetBySprintIdAsync(sprint.Id, CancellationToken.None).Result)
            task.SetSprint(null);
    }
}
