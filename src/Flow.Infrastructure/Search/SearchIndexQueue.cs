using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Persistence;
using Flow.Shared.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Постановка источников в очередь индексации. Enqueue только копит запись в scope'е (как репозитории —
/// Add без своего SaveChanges), а пишет её в БД <see cref="UnitOfWork"/> — одной транзакцией с самой правкой.
/// Пишется не через EF, а INSERT ... ON CONFLICT DO UPDATE: у таблицы unique (SourceType, SourceId, Operation),
/// и повторная постановка того же источника должна обновлять строку, а не падать на дубле.
/// </summary>
internal sealed class SearchIndexQueue(FlowDbContext db, SearchOptions options) : ISearchIndexQueue
{
    private readonly List<PendingRequest> _pending = [];

    public void Enqueue(SearchSourceType sourceType, Guid sourceId, Guid? boardId, SearchIndexOperation operation, int priority = 0)
    {
        // Поиск выключен — индекс не наполняется и поведение остальных эндпоинтов не меняется вовсе.
        if (!options.Enabled)
            return;

        var existing = _pending.FindIndex(r =>
            r.SourceType == sourceType && r.SourceId == sourceId && r.Operation == operation);

        if (existing >= 0)
        {
            _pending[existing] = _pending[existing] with { Priority = Math.Min(_pending[existing].Priority, priority) };
            return;
        }

        _pending.Add(new PendingRequest(sourceType, sourceId, boardId, operation, priority));
    }

    public bool HasPending => _pending.Count > 0;

    /// <summary>Пишет накопленное. Вызывается из UnitOfWork внутри той же транзакции, что и SaveChangesAsync.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
            return;

        var now = DateTime.UtcNow;

        foreach (var request in _pending)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("id", Guid.NewGuid()),
                new NpgsqlParameter("sourceType", (int)request.SourceType),
                new NpgsqlParameter("sourceId", request.SourceId),
                new NpgsqlParameter("boardId", NpgsqlDbType.Uuid) { Value = (object?)request.BoardId ?? DBNull.Value },
                new NpgsqlParameter("operation", (int)request.Operation),
                new NpgsqlParameter("priority", request.Priority),
                new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now }
            };

            // Живая правка поверх записи массовой переиндексации понижает приоритет до своего и сбрасывает
            // счётчик попыток: содержимое источника изменилось, прошлые ошибки к нему уже не относятся.
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "SearchIndexQueue"
                    ("Id", "SourceType", "SourceId", "BoardId", "Operation", "Priority", "EnqueuedAt", "AttemptCount", "NextAttemptAt", "LastError")
                VALUES (@id, @sourceType, @sourceId, @boardId, @operation, @priority, @now, 0, @now, NULL)
                ON CONFLICT ("SourceType", "SourceId", "Operation") DO UPDATE SET
                    "BoardId" = COALESCE(EXCLUDED."BoardId", "SearchIndexQueue"."BoardId"),
                    "Priority" = LEAST(EXCLUDED."Priority", "SearchIndexQueue"."Priority"),
                    "EnqueuedAt" = EXCLUDED."EnqueuedAt",
                    "AttemptCount" = 0,
                    "NextAttemptAt" = EXCLUDED."NextAttemptAt",
                    "LastError" = NULL
                """,
                parameters,
                cancellationToken);
        }

        _pending.Clear();
    }

    private sealed record PendingRequest(
        SearchSourceType SourceType,
        Guid SourceId,
        Guid? BoardId,
        SearchIndexOperation Operation,
        int Priority);
}
