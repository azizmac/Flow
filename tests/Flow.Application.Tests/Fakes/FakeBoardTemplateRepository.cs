using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeBoardTemplateRepository : IBoardTemplateRepository
{
    public List<BoardTemplate> Items { get; } = [];

    public Task<BoardTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<BoardTemplate>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BoardTemplate>>(Items.OrderBy(t => t.Name).ToList());

    public void Add(BoardTemplate template) => Items.Add(template);

    public void Remove(BoardTemplate template) => Items.Remove(template);
}
