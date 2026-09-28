using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class BoardTemplateRepository(FlowDbContext db) : IBoardTemplateRepository
{
    public Task<BoardTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.BoardTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BoardTemplate>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.BoardTemplates.OrderBy(t => t.Name).AsNoTracking().ToListAsync(cancellationToken);

    public void Add(BoardTemplate template) => db.BoardTemplates.Add(template);

    public void Remove(BoardTemplate template) => db.BoardTemplates.Remove(template);
}
