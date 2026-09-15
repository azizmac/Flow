using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Search;

/// <summary>Один чанк источника: что ляжет в Content и что уйдёт в эмбеддер (с шапкой).</summary>
internal sealed record SourceChunk(int Index, string Content, string EmbeddingInput);

/// <summary>Состояние источника на момент индексации.</summary>
internal sealed record SourceSnapshot(
    Guid? BoardId,
    bool IsClosed,
    DateTime SourceUpdatedAt,
    IReadOnlyList<SourceChunk> Chunks);

/// <summary>
/// Превращает источник в чанки. Шапка (ChunkHeaderBuilder) добавляется только к тексту для эмбеддера:
/// в Content её нет, иначе выдача показывала бы «Проект «Х» · PROJ-1 · комментарий» вместо самого текста,
/// а лексический tsvector считал бы название проекта в каждом чанке.
/// </summary>
internal sealed class SearchSourceReader(FlowDbContext db, SearchOptions options)
{
    /// <summary>null — источника больше нет: воркер трактует это как удаление из индекса.</summary>
    public Task<SourceSnapshot?> ReadAsync(SearchSourceType sourceType, Guid sourceId, CancellationToken cancellationToken) =>
        sourceType switch
        {
            SearchSourceType.Task => ReadTaskAsync(sourceId, cancellationToken),
            SearchSourceType.Comment => ReadCommentAsync(sourceId, cancellationToken),
            SearchSourceType.Board => ReadBoardAsync(sourceId, cancellationToken),
            SearchSourceType.User => ReadUserAsync(sourceId, cancellationToken),
            // Attachment заведён заранее: извлечение текста из вложений — этапы 7–8.
            _ => Task.FromResult<SourceSnapshot?>(null)
        };

    private async Task<SourceSnapshot?> ReadTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.TaskItems
            .AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new
            {
                t.Id,
                t.BoardId,
                t.Code,
                t.Title,
                t.Description,
                t.CreatedAt,
                IsClosed = db.Statuses.Any(s => s.Id == t.StatusId && s.IsFinal)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null)
            return null;

        // Отдельного UpdatedAt у задачи нет — журнал и есть история правок, его последняя запись и есть «когда меняли».
        var lastActivityAt = await db.TaskActivities
            .AsNoTracking()
            .Where(a => a.TaskId == taskId)
            .MaxAsync(a => (DateTime?)a.CreatedAt, cancellationToken);

        var header = ChunkHeaderBuilder.ForTask(task.Code.Value, task.Title);
        var content = string.IsNullOrWhiteSpace(task.Description)
            ? task.Title
            : $"{task.Title}\n\n{task.Description}";

        return new SourceSnapshot(
            task.BoardId,
            task.IsClosed,
            lastActivityAt ?? task.CreatedAt,
            Chunk(header, content));
    }

    private async Task<SourceSnapshot?> ReadCommentAsync(Guid commentId, CancellationToken cancellationToken)
    {
        var comment = await db.TaskComments
            .AsNoTracking()
            .Where(c => c.Id == commentId)
            .Join(db.TaskItems, c => c.TaskId, t => t.Id, (c, t) => new { Comment = c, Task = t })
            .Join(db.Boards, x => x.Task.BoardId, b => b.Id, (x, b) => new
            {
                x.Comment.Body,
                x.Comment.CreatedAt,
                x.Comment.EditedAt,
                x.Task.BoardId,
                x.Task.Code,
                BoardName = b.Name
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (comment is null)
            return null;

        var header = ChunkHeaderBuilder.ForComment(comment.BoardName, comment.Code.Value);

        // Комментарий закрытой задачи закрытым не считается: IsClosed — про сам источник, а фильтр
        // «только открытые» на этапе 4 всё равно идёт по задачам.
        return new SourceSnapshot(
            comment.BoardId,
            IsClosed: false,
            comment.EditedAt ?? comment.CreatedAt,
            Chunk(header, comment.Body));
    }

    private async Task<SourceSnapshot?> ReadBoardAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var board = await db.Boards
            .AsNoTracking()
            .Where(b => b.Id == boardId)
            .Select(b => new { b.Id, b.Name, b.Key, b.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (board is null)
            return null;

        var header = ChunkHeaderBuilder.ForBoard(board.Name, board.Key);

        return new SourceSnapshot(
            board.Id,
            IsClosed: false,
            board.CreatedAt,
            Chunk(header, $"{board.Name} ({board.Key})"));
    }

    private async Task<SourceSnapshot?> ReadUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return null;

        var header = ChunkHeaderBuilder.ForUser(user.Username, user.FullName);

        var lines = new List<string> { $"@{user.Username}", user.FullName };
        if (!string.IsNullOrWhiteSpace(user.JobTitle))
            lines.Add(user.JobTitle);
        lines.AddRange(user.Links.Select(link => $"{link.Type}: {link.Url}"));

        return new SourceSnapshot(
            BoardId: null,
            IsClosed: false,
            user.StatusChangedAt ?? user.CreatedAt,
            Chunk(header, string.Join("\n", lines)));
    }

    private List<SourceChunk> Chunk(string header, string content)
    {
        var pieces = TextChunker.Split(content, options.Indexing.ChunkTokens, options.Indexing.ChunkOverlap);

        return pieces
            .Select((piece, index) => new SourceChunk(index, piece, $"{header}\n{piece}"))
            .ToList();
    }
}
