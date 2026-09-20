using System.Buffers;
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

        var (hash, contentType, width, height) = await InspectAsync(request, cancellationToken);

        if (await attachments.FindDuplicateAsync(task.Id, hash, cancellationToken) is { } existing)
            return AttachmentUploadResult.Duplicate(existing.Id);

        var attachment = Attachment.Create(
            task.Id, task.BoardId, request.FileName, contentType, request.SizeBytes, hash, actor.Id, width, height);

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
    /// Один проход по файлу: считается SHA-256, попутно запоминается начало — для проверки сигнатуры
    /// и для размеров картинки. Тип берётся по расширению, но если файл притворяется картинкой —
    /// понижается до нейтрального, и тогда он уже никогда не будет показан inline.
    ///
    /// Голова теперь размером с ImageDimensions.HeadLength, а не с сигнатуру: у JPEG до SOF лежат
    /// APPn, и EXIF с миниатюрой внутри легко занимает десятки килобайт.
    /// </summary>
    private async Task<(byte[] Hash, string ContentType, int? Width, int? Height)> InspectAsync(
        AttachmentUploadCommand request, CancellationToken cancellationToken)
    {
        request.Content.Position = 0;

        // Из пула, а не новым массивом на каждую загрузку: 64 КиБ на файл — заметная нагрузка на кучу.
        var head = ArrayPool<byte>.Shared.Rent(ImageDimensions.HeadLength);
        try
        {
            // Rent вправе вернуть массив БОЛЬШЕ запрошенного, поэтому предел берём свой, а не head.Length:
            // иначе бюджет головы молча вырастет до размера, который вернул пул.
            var budget = ImageDimensions.HeadLength;
            var headLength = 0;

            using var sha = SHA256.Create();
            var buffer = new byte[81920];
            int read;

            while ((read = await request.Content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (headLength < budget)
                {
                    var take = Math.Min(budget - headLength, read);
                    buffer.AsSpan(0, take).CopyTo(head.AsSpan(headLength));
                    headLength += take;
                }

                sha.TransformBlock(buffer, 0, read, null, 0);
            }

            sha.TransformFinalBlock([], 0, 0);

            // ВСЕГДА срез по headLength, и ни разу по head.Length. Пул массив не чистит, и в хвосте
            // арендованного буфера лежат байты ПРЕДЫДУЩЕЙ загрузки. Шестибайтный файл «FF D8 FF E0 A0 26»
            // проходит проверку сигнатуры, а объявленная длина сегмента уводит разбор в этот хвост —
            // и в ответе оказываются размеры чужой картинки.
            var declared = FileTypes.FromFileName(request.FileName);
            var inline = options.InlineContentTypes.Contains(declared, StringComparer.OrdinalIgnoreCase);
            var contentType = inline && !FileTypes.MatchesSignature(declared, head.AsSpan(0, headLength))
                ? FileTypes.Fallback
                : declared;

            // После сверки сигнатуры и с уже разрешённым типом: у файла, понижённого до Fallback,
            // размеров быть не должно — он не картинка, чем бы ни притворялся.
            int? width = null;
            int? height = null;
            if (ImageDimensions.TryRead(contentType, head.AsSpan(0, headLength), out var w, out var h))
            {
                width = w;
                height = h;
            }

            return (sha.Hash!, contentType, width, height);
        }
        finally
        {
            // clearArray: страховка второго рубежа к дисциплине срезов выше. Обнуление 64 КиБ рядом
            // с хешированием всего файла не стоит ничего, а класс ошибок закрывает целиком.
            ArrayPool<byte>.Shared.Return(head, clearArray: true);
        }
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
