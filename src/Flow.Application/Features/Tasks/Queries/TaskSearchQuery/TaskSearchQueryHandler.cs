using System.Globalization;
using System.Text;
using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using DomainStatusType = Flow.Domain.Entities.StatusType;

namespace Flow.Application.Features.Tasks.Queries.TaskSearchQuery;

internal sealed class TaskSearchQueryHandler(ITaskItemRepository tasks, ITaskCommentRepository comments)
    : IRequestHandler<TaskSearchQuery, TaskListResponse>
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public async Task<TaskListResponse> Handle(TaskSearchQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);
        var (beforeCreatedAt, beforeId) = DecodeCursor(request.Cursor);

        var filter = new TaskListFilter(
            request.BoardId,
            request.AssigneeId,
            request.Unassigned,
            request.StatusId,
            (DomainStatusType?)request.StatusType,
            request.Query,
            beforeCreatedAt,
            beforeId,
            limit);

        var items = await tasks.SearchAsync(filter, cancellationToken);
        var counted = await tasks.CountAsync(filter, cancellationToken);

        // Один GROUP BY на всю страницу, не N+1 (как в TaskListQueryHandler).
        var counts = await comments.CountByTaskIdsAsync(items.Select(t => t.Id).ToList(), cancellationToken);

        // Страница заполнилась целиком — возможно, есть ещё; неполная страница всегда последняя.
        var last = items.Count == limit ? items[^1] : null;

        return new TaskListResponse(
            items.Select(t => t.ToResponse(counts.GetValueOrDefault(t.Id))).ToList(),
            last is null ? null : EncodeCursor(last.CreatedAt, last.Id),
            counted.Total,
            counted.ByType.Select(x => new StatusTypeCount((Shared.Contracts.Boards.StatusType?)x.Type, x.Count)).ToList(),
            counted.ByStatus.Select(x => new StatusCount(x.StatusId, x.Count)).ToList());
    }

    // Курсор — позиция последней выданной задачи в порядке (CreatedAt desc, Id desc). Формат внутренний:
    // клиент передаёт строку обратно как есть и не разбирает её.
    private static string EncodeCursor(DateTime createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt.Ticks}:{id}"));

    private static (DateTime?, Guid?) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return (null, null);

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':', 2);
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                && Guid.TryParse(parts[1], out var id))
            {
                return (new DateTime(ticks, DateTimeKind.Utc), id);
            }
        }
        catch (FormatException)
        {
            // Курсор пришёл битым (руками из адресной строки) — отдаём первую страницу вместо 400.
        }

        return (null, null);
    }
}
