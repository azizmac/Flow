using Flow.Application.Abstractions;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Scoped-накопитель постановок в очередь. Хендлер зовёт <see cref="Enqueue"/> рядом с изменением сущности,
/// а реальная запись уходит в БД в <see cref="UnitOfWork"/> — в той же транзакции, что и сама правка.
/// Поэтому «задача сохранилась, а в очередь не попала» невозможно, а откат транзакции уносит и очередь.
///
/// Почему не <c>db.Add</c>, как в репозиториях: у очереди есть unique (SourceType, SourceId, Operation),
/// и вторая правка той же задачи до разбора очереди уронила бы SaveChanges конфликтом. Нужен UPSERT,
/// а его EF не выражает — отсюда сырой INSERT ... ON CONFLICT DO UPDATE на том же соединении.
///
/// Публичный, а не internal, только ради сигнатуры <see cref="UnitOfWork"/>: снаружи Flow.Infrastructure
/// с очередью работают через <see cref="ISearchIndexQueue"/>.
/// </summary>
public sealed class SearchIndexQueue(FlowDbContext db, SearchOptions options) : ISearchIndexQueue
{
    private const string UpsertSql =
        """
        INSERT INTO "SearchIndexQueue"
            ("Id", "SourceType", "SourceId", "BoardId", "Operation", "Priority",
             "EnqueuedAt", "AttemptCount", "NextAttemptAt", "LastError")
        VALUES (@id, @sourceType, @sourceId, @boardId, @operation, @priority, @now, 0, @now, NULL)
        ON CONFLICT ("SourceType", "SourceId", "Operation") DO UPDATE SET
            "BoardId" = EXCLUDED."BoardId",
            "EnqueuedAt" = EXCLUDED."EnqueuedAt",
            "Priority" = LEAST("SearchIndexQueue"."Priority", EXCLUDED."Priority"),
            "NextAttemptAt" = EXCLUDED."NextAttemptAt",
            "AttemptCount" = 0,
            "LastError" = NULL
        """;

    private readonly List<PendingRequest> _pending = [];

    public bool HasPending => _pending.Count > 0;

    public void Enqueue(
        SearchSourceType sourceType,
        Guid sourceId,
        Guid? boardId,
        SearchIndexOperation operation,
        int priority = 0)
    {
        // Выключенный поиск не копит очередь: иначе после года с Enabled=false первое включение
        // получило бы миллион записей, половина которых указывает на уже удалённые источники.
        if (!options.Enabled)
            return;

        var existing = _pending.FindIndex(p =>
            p.SourceType == sourceType && p.SourceId == sourceId && p.Operation == operation);

        if (existing >= 0)
        {
            _pending[existing] = _pending[existing] with { Priority = Math.Min(_pending[existing].Priority, priority) };
            return;
        }

        _pending.Add(new PendingRequest(sourceType, sourceId, boardId, operation, priority));
    }

    /// <summary>Зовётся только из <see cref="UnitOfWork"/>, после успешного SaveChanges и до коммита транзакции.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
            return;

        var now = DateTime.UtcNow;

        foreach (var request in _pending)
        {
            // Параметры создаются вручную с явным NpgsqlDbType: BoardId бывает null (люди не принадлежат
            // проекту), а по одному только DBNull Postgres тип параметра вывести не может.
            NpgsqlParameter[] parameters =
            [
                new("id", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() },
                new("sourceType", NpgsqlDbType.Integer) { Value = (int)request.SourceType },
                new("sourceId", NpgsqlDbType.Uuid) { Value = request.SourceId },
                new("boardId", NpgsqlDbType.Uuid) { Value = (object?)request.BoardId ?? DBNull.Value },
                new("operation", NpgsqlDbType.Integer) { Value = (int)request.Operation },
                new("priority", NpgsqlDbType.Integer) { Value = request.Priority },
                new("now", NpgsqlDbType.TimestampTz) { Value = now }
            ];

            await db.Database.ExecuteSqlRawAsync(UpsertSql, parameters, cancellationToken);
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
