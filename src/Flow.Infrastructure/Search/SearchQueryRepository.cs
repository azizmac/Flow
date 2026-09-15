using System.Text;
using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Persistence;
using Flow.Shared.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Гибридная выдача одним запросом. Обе половины — векторная (HNSW) и полнотекстовая (GIN) — берут
/// свои топ-N чанков, дальше слияние RRF, свёртка чанков в источники и подсветка. Один SQL, а не два
/// параллельных: так это один заход в базу и одно соединение из пула, а обе половины и без того
/// укладываются в десятки миллисекунд на своих индексах.
/// </summary>
internal sealed class SearchQueryRepository(FlowDbContext db, IEmbeddingGenerator embedder, SearchOptions options)
    : ISearchQueryRepository
{
    /// <summary>
    /// Условие, одинаковое для обеих половин: своя версия модели, выбранные типы, проект и архив.
    /// Чанки прошлой ModelVersion лежат рядом до конца переиндексации — искать по ним нельзя,
    /// они из другого векторного пространства.
    /// </summary>
    private const string CommonFilter =
        """
        c."ModelVersion" = @model
            AND c."SourceType" = ANY(@types)
            AND (@boardId::uuid IS NULL OR c."BoardId" = @boardId)
            AND (@includeArchived OR c."IsClosed" = false)
        """;

    public async Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        var useVector = criteria.QueryEmbedding is not null;
        if (!useVector && !criteria.UseText)
            return new SearchPage([], 0);

        var sql = BuildSql(useVector, criteria.UseText);
        var parameters = BuildParameters(criteria);

        List<SearchRow> rows;
        if (useVector)
        {
            // ef_search и iterative_scan — сессионные настройки pgvector, поэтому нужна транзакция
            // с SET LOCAL. iterative_scan обязателен из-за фильтров: без него HNSW отдаёт свои N
            // ближайших, и после WHERE по типу и проекту от них может не остаться почти ничего.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                $"SET LOCAL hnsw.ef_search = {Math.Clamp(options.Query.HnswEfSearch, 1, 1000)}; " +
                "SET LOCAL hnsw.iterative_scan = relaxed_order;",
                cancellationToken);

            rows = await db.Database.SqlQueryRaw<SearchRow>(sql, parameters).ToListAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            rows = await db.Database.SqlQueryRaw<SearchRow>(sql, parameters).ToListAsync(cancellationToken);
        }

        var items = rows
            .Select(row => new SearchHit(
                (SearchSourceType)row.SourceType,
                row.SourceId,
                row.BoardId,
                row.Title,
                row.Snippet,
                row.Score,
                row.TaskCode,
                row.UpdatedAt,
                row.ParentId))
            .ToArray();

        return new SearchPage(items, rows.Count == 0 ? 0 : (int)rows[0].Total);
    }

    private static string BuildSql(bool useVector, bool useText)
    {
        var sql = new StringBuilder();
        var halves = new List<string>();

        sql.Append("WITH ");

        if (useVector)
        {
            // Окно считается по уже урезанной выборке: row_number поверх LIMIT, а не наоборот —
            // иначе PostgreSQL отсортировал бы весь индекс и HNSW стал бы бесполезен.
            sql.Append(
                $"""
                 vector_hits AS (
                     SELECT v."Id", row_number() OVER (ORDER BY v.distance) AS rank
                     FROM (
                         SELECT c."Id", c."Embedding" <=> @query AS distance
                         FROM "SearchChunks" c
                         WHERE {CommonFilter}
                         ORDER BY c."Embedding" <=> @query
                         LIMIT @vectorTopN
                     ) v
                 )
                 """);
            halves.Add("SELECT \"Id\" AS id, 1.0 / (@rrfK + rank) AS score FROM vector_hits");
        }

        if (useText)
        {
            if (useVector)
                sql.Append(",\n");

            sql.Append(
                $"""
                 text_hits AS (
                     SELECT t."Id", row_number() OVER (ORDER BY t.text_rank DESC) AS rank
                     FROM (
                         SELECT c."Id", ts_rank_cd(c."Tsv", tsq) AS text_rank
                         FROM "SearchChunks" c, websearch_to_tsquery('russian', @text) tsq
                         WHERE {CommonFilter} AND c."Tsv" @@ tsq
                         ORDER BY ts_rank_cd(c."Tsv", tsq) DESC
                         LIMIT @textTopN
                     ) t
                 )
                 """);
            halves.Add("SELECT \"Id\" AS id, 1.0 / (@rrfK + rank) AS score FROM text_hits");
        }

        // RRF: складываются обратные ранги, а не сами оценки — косинус и ts_rank несравнимы.
        sql.Append(",\nmerged AS (\n    SELECT id, SUM(score) AS score FROM (\n        ");
        sql.Append(string.Join("\n        UNION ALL\n        ", halves));
        sql.Append("\n    ) both_halves GROUP BY id\n)");

        // Свёртка чанков в источники: у источника остаётся его лучший чанк, он же идёт в подсветку.
        sql.Append(
            """
            ,
            best AS (
                SELECT DISTINCT ON (c."SourceType", c."SourceId")
                       c."SourceType", c."SourceId", c."BoardId", c."Content", c."SourceUpdatedAt", m.score
                FROM merged m JOIN "SearchChunks" c ON c."Id" = m.id
                ORDER BY c."SourceType", c."SourceId", m.score DESC
            )
            SELECT b."SourceType" AS "SourceType",
                   b."SourceId" AS "SourceId",
                   b."BoardId" AS "BoardId",
                   COALESCE(ti."Title", ct."Title", bd."Name",
                            NULLIF(btrim(COALESCE(u."FirstName", '') || ' ' || COALESCE(u."LastName", '')), ''),
                            u."Username", '') AS "Title",
                   ts_headline('russian', b."Content", websearch_to_tsquery('russian', @text),
                               'MaxFragments=2, MinWords=5, MaxWords=20, StartSel=<mark>, StopSel=</mark>') AS "Snippet",
                   b.score::double precision AS "Score",
                   COALESCE(ti."Code", ct."Code") AS "TaskCode",
                   b."SourceUpdatedAt" AS "UpdatedAt",
                   cm."TaskId" AS "ParentId",
                   count(*) OVER () AS "Total"
            FROM best b
            LEFT JOIN "TaskItems" ti ON b."SourceType" = 1 AND ti."Id" = b."SourceId"
            LEFT JOIN "TaskComments" cm ON b."SourceType" = 2 AND cm."Id" = b."SourceId"
            LEFT JOIN "TaskItems" ct ON ct."Id" = cm."TaskId"
            LEFT JOIN "Boards" bd ON b."SourceType" = 3 AND bd."Id" = b."SourceId"
            LEFT JOIN "Users" u ON b."SourceType" = 4 AND u."Id" = b."SourceId"
            WHERE b."SourceType" <> 4 OR u."Status" <> 2
            ORDER BY b.score DESC, b."SourceUpdatedAt" DESC
            LIMIT @limit OFFSET @offset
            """);

        return sql.ToString();
    }

    private NpgsqlParameter[] BuildParameters(SearchCriteria criteria)
    {
        var parameters = new List<NpgsqlParameter>
        {
            new("model", embedder.ModelVersion),
            new("types", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = criteria.Types.Select(type => (int)type).ToArray() },
            new("boardId", NpgsqlDbType.Uuid) { Value = (object?)criteria.BoardId ?? DBNull.Value },
            new("includeArchived", criteria.IncludeArchived),
            new("text", criteria.Query),
            new("rrfK", criteria.RrfK),
            new("limit", criteria.Limit),
            new("offset", criteria.Offset)
        };

        if (criteria.QueryEmbedding is { } embedding)
        {
            parameters.Add(new NpgsqlParameter("query", new HalfVector(embedding.Select(value => (Half)value).ToArray())));
            parameters.Add(new NpgsqlParameter("vectorTopN", criteria.VectorTopN));
        }

        if (criteria.UseText)
            parameters.Add(new NpgsqlParameter("textTopN", criteria.TextTopN));

        return parameters.ToArray();
    }

    /// <summary>Строка выдачи: имена свойств совпадают с псевдонимами столбцов в SQL.</summary>
    private sealed class SearchRow
    {
        public int SourceType { get; init; }

        public Guid SourceId { get; init; }

        public Guid? BoardId { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Snippet { get; init; } = string.Empty;

        public double Score { get; init; }

        public string? TaskCode { get; init; }

        public DateTime UpdatedAt { get; init; }

        /// <summary>Задача комментария; у остальных типов null.</summary>
        public Guid? ParentId { get; init; }

        /// <summary>Одинаковый во всех строках: count(*) OVER () до LIMIT.</summary>
        public long Total { get; init; }
    }
}
