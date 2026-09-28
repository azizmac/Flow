using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeTaskTemplateRepository : ITaskTemplateRepository
{
    public List<TaskTemplate> Items { get; } = [];

    public Task<TaskTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<TaskTemplate>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskTemplate>>(Items.Where(t => t.BoardId == boardId).OrderBy(t => t.SortOrder).ToList());

    public void Add(TaskTemplate template) => Items.Add(template);

    public void Remove(TaskTemplate template) => Items.Remove(template);
}
