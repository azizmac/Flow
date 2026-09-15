using Flow.Application.Features.Search;
using Flow.Infrastructure.Persistence;
using Flow.Shared.Contracts.Search;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Превращает источник (задачу, комментарий, проект, человека) в чанки: чистый текст для БД и текст
/// с контекстной шапкой для эмбеддера. Шапка в БД не дублируется — подсветка и полнотекстовый поиск
/// идут по Content, контекст нужен только вектору.
/// </summary>
internal sealed class SearchSourceReader(FlowDbContext db, SearchOptions options)
{
    /// <summary>null — источника больше нет (удалён, пока запись ждала в очереди): чанки просто убираются.</summary>
    public Task<SourceSnapshot?> ReadAsync(SearchSourceType sourceType, Guid sourceId, CancellationToken cancellationToken) =>
        sourceType switch
        {
            SearchSourceType.Task => ReadTaskAsync(sourceId, cancellationToken),
            SearchSourceType.Comment => ReadCommentAsync(sourceId, cancellationToken),
            SearchSourceType.Board => ReadBoardAsync(sourceId, cancellationToken),
            SearchSourceType.User => ReadUserAsync(sourceId, cancellationToken),
            // Вложения появятся вместе с ТЗ вложений (этапы 7–8) — до тех пор их никто не ставит в очередь.
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
internal sealed record SourceSnapshot(Guid? BoardId, bool IsClosed, DateTime SourceUpdatedAt, IReadOnlyList<SourceChunk> Chunks);
