using System.Security.Cryptography;
using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;

/// <summary>
/// Порядок важен: сначала объект в хранилище, потом строка в БД. Обратный порядок дал бы вложения,
/// которые видно в списке, но нельзя скачать; лишний объект в бакете дешевле битой строки в интерфейсе.
/// Если транзакция всё же упала, объект удаляется best-effort.
/// </summary>
internal sealed class AttachmentUploadCommandHandler(
    ITaskItemRepository tasks,
    IAttachmentRepository attachments,
    ITaskActivityRepository activities,
    IFileStorage storage,
    ISearchIndexQueue searchIndex,
    AttachmentOptions options,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AttachmentUploadCommand, AttachmentUploadResult>
{
    public async Task<AttachmentUploadResult> Handle(AttachmentUploadCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanAttach(actor);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return AttachmentUploadResult.TaskNotFound();

        if (Validate(request) is { } error)
            return AttachmentUploadResult.Invalid(error);

        var usage = await attachments.GetUsageAsync(task.Id, cancellationToken);
        if (usage.Count >= options.MaxPerTask)
            return AttachmentUploadResult.Invalid($"К задаче уже приложено {options.MaxPerTask} файлов — больше нельзя.");

        if (usage.TotalBytes + request.SizeBytes > options.MaxTotalBytesPerTask)
            return AttachmentUploadResult.Invalid($"Суммарный размер вложений задачи не может превышать {Megabytes(options.MaxTotalBytesPerTask)} МБ.");

        var (hash, contentType) = await InspectAsync(request, cancellationToken);

        if (await attachments.FindDuplicateAsync(task.Id, hash, cancellationToken) is { } existing)
            return AttachmentUploadResult.Duplicate(existing.Id);

        var attachment = Attachment.Create(task.Id, task.BoardId, request.FileName, contentType, request.SizeBytes, hash, actor.Id);

        request.Content.Position = 0;
        await storage.PutAsync(attachment.StorageKey, request.Content, attachment.ContentType, cancellationToken);

        try
        {
            attachments.Add(attachment);
            activities.Add(TaskActivity.AttachmentAdded(task.Id, actor.Id, attachment.Id, attachment.FileName));

            // Очередь индексации пишется той же транзакцией: содержимое файла воркер вытащит сам,
            // здесь незачем ни читать его второй раз, ни ждать модель.
            searchIndex.Enqueue(SearchSourceType.Attachment, attachment.Id, attachment.BoardId, SearchIndexOperation.Upsert);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Объект уже в бакете, а строки не будет — убираем за собой, чтобы не плодить мусор.
            await SafeDeleteAsync(attachment.StorageKey, cancellationToken);
            throw;
        }

        return AttachmentUploadResult.Success(attachment.ToResponse(options));
    }

    private string? Validate(AttachmentUploadCommand request)
    {
        if (request.SizeBytes <= 0)
            return "Пустой файл приложить нельзя.";

        if (request.SizeBytes > options.MaxFileBytes)
            return $"Файл больше {Megabytes(options.MaxFileBytes)} МБ.";

        var extension = FileTypes.Extension(request.FileName);
        return options.BlockedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? $"Файлы {extension} прикладывать нельзя."
            : null;
    }

    /// <summary>
    /// Один проход по файлу: считается SHA-256, попутно запоминается начало для проверки сигнатуры.
    /// Тип берётся по расширению, но если файл притворяется картинкой — понижается до нейтрального,
    /// и тогда он уже никогда не будет показан inline.
    /// </summary>
    private async Task<(byte[] Hash, string ContentType)> InspectAsync(AttachmentUploadCommand request, CancellationToken cancellationToken)
    {
        request.Content.Position = 0;

        var head = new byte[FileTypes.SignatureLength];
        var headLength = 0;

        using var sha = SHA256.Create();
        var buffer = new byte[81920];
        int read;

        while ((read = await request.Content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (headLength < head.Length)
            {
                var take = Math.Min(head.Length - headLength, read);
                buffer.AsSpan(0, take).CopyTo(head.AsSpan(headLength));
                headLength += take;
            }

            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);

        var declared = FileTypes.FromFileName(request.FileName);
        var inline = options.InlineContentTypes.Contains(declared, StringComparer.OrdinalIgnoreCase);
        var contentType = inline && !FileTypes.MatchesSignature(declared, head.AsSpan(0, headLength))
            ? FileTypes.Fallback
            : declared;

        return (sha.Hash!, contentType);
    }

    private async Task SafeDeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(key, cancellationToken);
        }
        catch
        {
            // Хранилище недоступно — объект останется мусором; ронять из-за этого исходную ошибку незачем.
        }
    }

    private static long Megabytes(long bytes) => bytes / (1024 * 1024);
}
