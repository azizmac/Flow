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

    public void Add(TaskActivity activity) => _activities.Add(activity);
}
