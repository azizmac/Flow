using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeTaskActivityRepository : ITaskActivityRepository
{
    private readonly List<TaskActivity> _activities = [];

    public IReadOnlyList<TaskActivity> All => _activities;

    public IReadOnlyList<TaskActivity> ForTask(Guid taskId) => _activities.Where(a => a.TaskId == taskId).ToList();

    public Task<IReadOnlyList<TaskActivity>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskActivity>>(ForTask(taskId).OrderBy(a => a.CreatedAt).ToList());

    public Task<IReadOnlyList<TaskActivity>> GetByTaskIdsAsync(
        IReadOnlyCollection<Guid> taskIds, IReadOnlyCollection<TaskActivityType> types, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskActivity>>(_activities
            .Where(a => taskIds.Contains(a.TaskId) && types.Contains(a.Type))
            .OrderBy(a => a.CreatedAt)
            .ToList());

    public Task<IReadOnlyList<Guid>> GetTaskIdsWithValueAsync(TaskActivityType type, string value, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(_activities
            .Where(a => a.Type == type && (a.OldValue == value || a.NewValue == value))
            .Select(a => a.TaskId)
            .Distinct()
            .ToList());

    public void Add(TaskActivity activity) => _activities.Add(activity);
}
