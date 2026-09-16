using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Persistence;
using Flow.Infrastructure.Search.Extraction;
using Flow.Shared.Contracts.Search;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Превращает источник (задачу, комментарий, проект, человека) в чанки: чистый текст для БД и текст
/// с контекстной шапкой для эмбеддера. Шапка в БД не дублируется — подсветка и полнотекстовый поиск
/// идут по Content, контекст нужен только вектору.
/// </summary>
internal sealed class SearchSourceReader(
    FlowDbContext db,
    SearchOptions options,
    IFileStorage storage,
    ITextExtractor extractor,
    PdfPageImageExtractor pdfPages)
{
    /// <summary>null — источника больше нет (удалён, пока запись ждала в очереди): чанки просто убираются.</summary>
    public Task<SourceSnapshot?> ReadAsync(SearchSourceType sourceType, Guid sourceId, CancellationToken cancellationToken) =>
        sourceType switch
        {
            SearchSourceType.Task => ReadTaskAsync(sourceId, cancellationToken),
            SearchSourceType.Comment => ReadCommentAsync(sourceId, cancellationToken),
            SearchSourceType.Board => ReadBoardAsync(sourceId, cancellationToken),
            SearchSourceType.User => ReadUserAsync(sourceId, cancellationToken),
            SearchSourceType.Attachment => ReadAttachmentAsync(sourceId, cancellationToken),
            _ => throw new NotSupportedException($"Источник {sourceType} пока не индексируется.")
        };

    private async Task<SourceSnapshot?> ReadTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return null;

        var board = await db.Boards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == task.BoardId, cancellationToken);
        if (board is null)
            return null;

        // «Закрытость» — финальность статуса: в чанке она хранится флагом, и при смене статуса
        // обновляется UPDATE'ом, без повторного обращения к модели.
        var isClosed = await db.Statuses.AsNoTracking()
            .Where(s => s.Id == task.StatusId)
            .Select(s => s.IsFinal)
            .FirstOrDefaultAsync(cancellationToken);

        var header = ChunkHeaderBuilder.ForTask(task.Code.Value, task.Title);
        var text = string.IsNullOrWhiteSpace(task.Description)
            ? task.Title
            : $"{task.Title}\n\n{task.Description}";

        return new SourceSnapshot(task.BoardId, isClosed, task.CreatedAt, BuildChunks(header, text));
    }

    private async Task<SourceSnapshot?> ReadCommentAsync(Guid commentId, CancellationToken cancellationToken)
    {
        var found = await (
            from comment in db.TaskComments.AsNoTracking()
            join task in db.TaskItems.AsNoTracking() on comment.TaskId equals task.Id
            join board in db.Boards.AsNoTracking() on task.BoardId equals board.Id
            where comment.Id == commentId
            select new
            {
                comment.Body,
                comment.CreatedAt,
                comment.EditedAt,
                task.BoardId,
                TaskCode = task.Code,
                BoardName = board.Name
            }).FirstOrDefaultAsync(cancellationToken);

        if (found is null)
            return null;

        var header = ChunkHeaderBuilder.ForComment(found.BoardName, found.TaskCode.Value);

        // Комментарий к закрытой задаче «закрытым» не считается: IsClosed есть только у задач,
        // режим выдачи по архиву выбирается на этапе поиска.
        return new SourceSnapshot(found.BoardId, IsClosed: false, found.EditedAt ?? found.CreatedAt, BuildChunks(header, found.Body));
    }

    private async Task<SourceSnapshot?> ReadBoardAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var board = await db.Boards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);
        if (board is null)
            return null;

        var header = ChunkHeaderBuilder.ForBoard(board.Name, board.Key);

        // Проект — всегда один короткий чанк: нужен, чтобы сам проект находился в общем поиске.
        return new SourceSnapshot(board.Id, IsClosed: false, board.CreatedAt, BuildChunks(header, $"{board.Name}\n{board.Key}"));
    }

    private async Task<SourceSnapshot?> ReadUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.Links)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return null;

        var header = ChunkHeaderBuilder.ForUser(user.Username, user.FirstName, user.LastName);

        var lines = new List<string> { $"@{user.Username}", user.FullName.Trim() };
        lines.AddRange(user.Links.Select(link => $"{link.Type}: {link.Url}"));

        var text = string.Join('\n', lines.Where(line => line.Length > 0));

        // У человека доски нет: он находится в общем поиске, а не внутри проекта.
        return new SourceSnapshot(BoardId: null, IsClosed: false, user.CreatedAt, BuildChunks(header, text));
    }

    /// <summary>
    /// Вложение: текст достаётся из файла в хранилище. Если текста нет — скан без распознаваемого слоя,
    /// картинка, битый или защищённый паролем файл, — вложение всё равно попадает в индекс одним чанком
    /// с именем файла: «договор-2026.pdf» ищут не реже, чем то, что внутри.
    /// </summary>
    private async Task<SourceSnapshot?> ReadAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        var found = await (
            from attachment in db.Attachments.AsNoTracking()
            join task in db.TaskItems.AsNoTracking() on attachment.TaskId equals task.Id
            where attachment.Id == attachmentId
            select new
            {
                attachment.FileName,
                attachment.ContentType,
                attachment.StorageKey,
                attachment.SizeBytes,
                attachment.UploadedAt,
                attachment.BoardId,
                TaskCode = task.Code,
                task.StatusId
            }).FirstOrDefaultAsync(cancellationToken);

        if (found is null)
            return null;

        // Закрытость наследуется от задачи-владельца: иначе фильтр «без архива» врал бы на её файлах.
        var isClosed = await db.Statuses.AsNoTracking()
            .Where(s => s.Id == found.StatusId)
            .Select(s => s.IsFinal)
            .FirstOrDefaultAsync(cancellationToken);

        var text = await ExtractAsync(found.StorageKey, found.FileName, found.ContentType, cancellationToken);

        // Имя файла идёт первой строкой содержимого, а не только в шапке: шапка уходит в вектор,
        // а полнотекстовая ветка ищет по Content — без этого файл не найти по точному имени.
        var content = string.IsNullOrWhiteSpace(text) ? found.FileName : $"{found.FileName}\n\n{text}";
        var header = ChunkHeaderBuilder.ForAttachment(found.TaskCode.Value, found.FileName);

        // Кадры идут во вторую, визуальную половину индекса — параллельно текстовой, а не вместо неё:
        // по имени файла скриншот тоже должен находиться.
        var images = await ReadImagesAsync(found.StorageKey, found.FileName, found.ContentType, found.SizeBytes, text, cancellationToken);

        return new SourceSnapshot(found.BoardId, isClosed, found.UploadedAt, BuildChunks(header, content), images);
    }

    /// <summary>
    /// Кадры для визуальной модели: сама картинка либо страницы скана. Скан — это PDF, из которого
    /// не извлёкся текст: там, где текст есть, визуальная ветка не нужна и только тратила бы прогоны.
    /// SVG не берём — это разметка, а не растр. Крупные файлы пропускаем: прогон дорогой, а смысла
    /// в двадцатимегабайтном кадре не больше, чем в его уменьшенной копии.
    /// </summary>
    private async Task<IReadOnlyList<SourceImage>> ReadImagesAsync(
        string storageKey,
        string fileName,
        string contentType,
        long sizeBytes,
        string extractedText,
        CancellationToken cancellationToken)
    {
        var vision = options.Embeddings.Vision;
        if (!options.VisionEnabled || sizeBytes > vision.MaxBytes)
            return [];

        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                      && !contentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase);
        var isScan = string.IsNullOrWhiteSpace(extractedText) && PdfPageImageExtractor.IsPdf(fileName, contentType);

        if (!isImage && !isScan)
            return [];

        await using var content = await storage.OpenReadAsync(storageKey, cancellationToken);
        if (content is null)
            return [];

        if (isImage)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            return [new SourceImage(buffer.ToArray(), contentType, fileName)];
        }

        var pages = await pdfPages.ExtractAsync(content, fileName, cancellationToken);

        // Номер страницы виден в выдаче: у скана на несколько листов иначе непонятно, какой нашёлся.
        return pages
            .Select(page => new SourceImage(page.Content, page.ContentType, $"{fileName}, стр. {page.Number}"))
            .ToArray();
    }

    private async Task<string> ExtractAsync(string storageKey, string fileName, string contentType, CancellationToken cancellationToken)
    {
        if (!extractor.CanExtract(fileName, contentType))
            return string.Empty;

        // Объекта может не быть: строка есть, а файл пропал (сбой загрузки, чистка бакета руками).
        await using var content = await storage.OpenReadAsync(storageKey, cancellationToken);
        if (content is null)
            return string.Empty;

        // Исключения формата гасит CompositeTextExtractor: из-за одного битого файла очередь не встаёт.
        return await extractor.ExtractAsync(content, fileName, contentType, cancellationToken);
    }

    private IReadOnlyList<SourceChunk> BuildChunks(string header, string? text)
    {
        var indexing = options.Indexing;
        var chunks = TextChunker.Split(text, indexing.ChunkTokens, indexing.ChunkOverlap);

        return chunks
            .Select((content, index) => new SourceChunk(index, content, ChunkHeaderBuilder.Apply(header, content)))
            .ToArray();
    }
}

/// <param name="Content">Чистый текст чанка — он и попадает в БД.</param>
/// <param name="EmbeddingInput">Он же с шапкой — уходит в эмбеддер.</param>
internal sealed record SourceChunk(int Index, string Content, string EmbeddingInput);

/// <param name="SourceUpdatedAt">Момент версии источника, с которой сняты чанки.</param>
/// <param name="Images">Кадры вложения для визуальной половины индекса: сама картинка либо страницы
/// скана. Пусто — индексировать нечего (не картинка и не скан, модель выключена или файл велик).</param>
internal sealed record SourceSnapshot(
    Guid? BoardId,
    bool IsClosed,
    DateTime SourceUpdatedAt,
    IReadOnlyList<SourceChunk> Chunks,
    IReadOnlyList<SourceImage>? Images = null);

/// <param name="Content">Байты кадра — уходят в визуальную модель как есть, масштабирует она сама.</param>
/// <param name="Label">Что показать в выдаче: имя файла, а для скана — имя со страницей.</param>
internal sealed record SourceImage(byte[] Content, string ContentType, string Label);
