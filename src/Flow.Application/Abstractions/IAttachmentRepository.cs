using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

public interface IAttachmentRepository
{
    Task<Attachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Вложения задачи по возрастанию UploadedAt.</summary>
    Task<IReadOnlyList<Attachment>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Сколько уже приложено к задаче и на сколько байт — для лимитов, одним запросом.</summary>
    Task<AttachmentUsage> GetUsageAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Тот же файл в той же задаче: повтор отклоняется, между задачами дубли допустимы.</summary>
    Task<Attachment?> FindDuplicateAsync(Guid taskId, byte[] contentHash, CancellationToken cancellationToken);

    void Add(Attachment attachment);

    void Remove(Attachment attachment);
}

/// <param name="Count">Число вложений задачи.</param>
/// <param name="TotalBytes">Их суммарный размер.</param>
public sealed record AttachmentUsage(int Count, long TotalBytes);
