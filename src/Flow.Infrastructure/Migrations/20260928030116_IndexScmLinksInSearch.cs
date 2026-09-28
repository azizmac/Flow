using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndexScmLinksInSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Этап 5E: PR и коммиты, связанные до появления индексации, ставятся в очередь поиска фоновым приоритетом
            // (1, как массовая переиндексация). SourceType 6 = Development, Operation 1 = Upsert; ветки (Kind 0) — нет.
            migrationBuilder.Sql("""
                INSERT INTO "SearchIndexQueue"
                    ("Id", "SourceType", "SourceId", "BoardId", "Operation", "Priority", "EnqueuedAt", "AttemptCount", "NextAttemptAt", "LastError")
                SELECT gen_random_uuid(), 6, l."Id", t."BoardId", 1, 1, now(), 0, now(), NULL
                FROM "ScmLinks" l JOIN "TaskItems" t ON t."Id" = l."TaskId"
                WHERE l."Kind" <> 0
                ON CONFLICT ("SourceType", "SourceId", "Operation") DO NOTHING
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "SearchIndexQueue" WHERE "SourceType" = 6""");
            migrationBuilder.Sql("""DELETE FROM "SearchChunks" WHERE "SourceType" = 6""");
        }
    }
}
