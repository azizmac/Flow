using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class AttachmentRepository(FlowDbContext db) : IAttachmentRepository
{
    public Task<Attachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Attachment>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        await db.Attachments
            .Where(a => a.TaskId == taskId)
            .OrderBy(a => a.UploadedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    /// <summary>Счёт и сумма одним запросом: лимиты проверяются перед каждой загрузкой.</summary>
    public async Task<AttachmentUsage> GetUsageAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var usage = await db.Attachments
            .Where(a => a.TaskId == taskId)
            .GroupBy(a => 1)
            .Select(group => new { Count = group.Count(), TotalBytes = group.Sum(a => a.SizeBytes) })
            .FirstOrDefaultAsync(cancellationToken);

        return usage is null ? new AttachmentUsage(0, 0) : new AttachmentUsage(usage.Count, usage.TotalBytes);
    }

    public Task<Attachment?> FindDuplicateAsync(Guid taskId, byte[] contentHash, CancellationToken cancellationToken) =>
        db.Attachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TaskId == taskId && a.ContentHash == contentHash, cancellationToken);

    public void Add(Attachment attachment) => db.Attachments.Add(attachment);

    public void Remove(Attachment attachment) => db.Attachments.Remove(attachment);
}
