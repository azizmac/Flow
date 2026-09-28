using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class TaskRecurrenceRepository(FlowDbContext db) : ITaskRecurrenceRepository
{
    public Task<TaskRecurrence?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.TaskRecurrences.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<TaskRecurrence?> GetByTemplateAsync(Guid templateTaskId, CancellationToken cancellationToken) =>
        db.TaskRecurrences.FirstOrDefaultAsync(r => r.TemplateTaskId == templateTaskId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetActiveIdsAsync(CancellationToken cancellationToken) =>
        await db.TaskRecurrences.Where(r => r.IsActive).Select(r => r.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetTemplatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        (await db.TaskRecurrences.Where(r => taskIds.Contains(r.TemplateTaskId)).Select(r => r.TemplateTaskId).ToListAsync(cancellationToken)).ToHashSet();

    public Task<bool> OccurrenceExistsAsync(Guid recurrenceId, DateOnly occursOn, CancellationToken cancellationToken) =>
        db.TaskRecurrenceOccurrences.AnyAsync(o => o.RecurrenceId == recurrenceId && o.OccursOn == occursOn, cancellationToken);

    public void Add(TaskRecurrence recurrence) => db.TaskRecurrences.Add(recurrence);

    public void Remove(TaskRecurrence recurrence) => db.TaskRecurrences.Remove(recurrence);

    public void AddOccurrence(TaskRecurrenceOccurrence occurrence) => db.TaskRecurrenceOccurrences.Add(occurrence);
}
