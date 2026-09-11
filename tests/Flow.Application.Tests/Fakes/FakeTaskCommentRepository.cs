using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeTaskCommentRepository : ITaskCommentRepository
{
    private readonly List<TaskComment> _comments = [];

    public IReadOnlyList<TaskComment> All => _comments;

    public Task<TaskComment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_comments.SingleOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<TaskComment>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskComment>>(_comments.Where(c => c.TaskId == taskId).OrderBy(c => c.CreatedAt).ToList());

    public Task<IReadOnlyDictionary<Guid, int>> CountByTaskIdsAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(_comments
            .Where(c => taskIds.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .ToDictionary(g => g.Key, g => g.Count()));

    public void Add(TaskComment comment) => _comments.Add(comment);

    public void Remove(TaskComment comment) => _comments.Remove(comment);
}
