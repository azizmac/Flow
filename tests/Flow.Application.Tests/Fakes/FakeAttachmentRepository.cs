using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeAttachmentRepository : IAttachmentRepository
{
    private readonly List<Attachment> _attachments = [];

    public IReadOnlyList<Attachment> All => _attachments;

    public Task<Attachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_attachments.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<Attachment>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Attachment>>(
            _attachments.Where(a => a.TaskId == taskId).OrderBy(a => a.UploadedAt).ToList());

    public Task<AttachmentUsage> GetUsageAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var own = _attachments.Where(a => a.TaskId == taskId).ToList();
        return Task.FromResult(new AttachmentUsage(own.Count, own.Sum(a => a.SizeBytes)));
    }

    public Task<Attachment?> FindDuplicateAsync(Guid taskId, byte[] contentHash, CancellationToken cancellationToken) =>
        Task.FromResult(_attachments.FirstOrDefault(a => a.TaskId == taskId && a.ContentHash.SequenceEqual(contentHash)));

    public void Add(Attachment attachment) => _attachments.Add(attachment);

    public void Remove(Attachment attachment) => _attachments.Remove(attachment);
}

/// <summary>Хранилище в памяти: тесты проверяют поведение хендлеров, а не S3.</summary>
public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _objects = [];

    public IReadOnlyDictionary<string, byte[]> Objects => _objects;

    /// <summary>Префиксы, по которым просили удалить: каскад должен ходить в хранилище только когда есть что уносить.</summary>
    public List<string> PrefixDeletes { get; } = [];

    /// <summary>Выставить, чтобы проверить откат: объект в бакете есть, а строки не будет.</summary>
    public bool FailOnPut { get; set; }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        if (FailOnPut)
            throw new InvalidOperationException("Хранилище недоступно (фейк).");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _objects[key] = buffer.ToArray();
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(_objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        _objects.Remove(key);
        return Task.CompletedTask;
    }

    public Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        PrefixDeletes.Add(prefix);

        foreach (var key in _objects.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            _objects.Remove(key);

        return Task.CompletedTask;
    }
}
