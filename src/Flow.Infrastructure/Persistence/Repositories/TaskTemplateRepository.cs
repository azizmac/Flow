using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class TaskTemplateRepository(FlowDbContext db) : ITaskTemplateRepository
{
    public Task<TaskTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.TaskTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskTemplate>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        await db.TaskTemplates.Where(t => t.BoardId == boardId).OrderBy(t => t.SortOrder).ToListAsync(cancellationToken);

    public void Add(TaskTemplate template) => db.TaskTemplates.Add(template);

    public void Remove(TaskTemplate template) => db.TaskTemplates.Remove(template);
}
