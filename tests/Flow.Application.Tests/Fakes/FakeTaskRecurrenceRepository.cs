using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeTaskRecurrenceRepository : ITaskRecurrenceRepository
{
    private readonly List<TaskRecurrence> _recurrences = [];
    private readonly List<TaskRecurrenceOccurrence> _occurrences = [];

    public IReadOnlyList<TaskRecurrenceOccurrence> Occurrences => _occurrences;

    public Task<TaskRecurrence?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_recurrences.SingleOrDefault(r => r.Id == id));

    public Task<TaskRecurrence?> GetByTemplateAsync(Guid templateTaskId, CancellationToken cancellationToken) =>
        Task.FromResult(_recurrences.SingleOrDefault(r => r.TemplateTaskId == templateTaskId));

    public Task<IReadOnlyList<Guid>> GetActiveIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(_recurrences.Where(r => r.IsActive).Select(r => r.Id).ToList());

    public Task<IReadOnlySet<Guid>> GetTemplatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(_recurrences.Where(r => taskIds.Contains(r.TemplateTaskId)).Select(r => r.TemplateTaskId).ToHashSet());

    public Task<bool> OccurrenceExistsAsync(Guid recurrenceId, DateOnly occursOn, CancellationToken cancellationToken) =>
        Task.FromResult(_occurrences.Any(o => o.RecurrenceId == recurrenceId && o.OccursOn == occursOn));

    public void Add(TaskRecurrence recurrence) => _recurrences.Add(recurrence);

    public void Remove(TaskRecurrence recurrence) => _recurrences.Remove(recurrence);

    public void AddOccurrence(TaskRecurrenceOccurrence occurrence) => _occurrences.Add(occurrence);
}
