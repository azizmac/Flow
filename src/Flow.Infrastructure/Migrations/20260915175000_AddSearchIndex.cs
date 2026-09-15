using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Расширение ставится миграцией, а не docker/postgres/init.sql: тот выполняется только при
            // первичной инициализации тома, а тома уже созданы. Образ pgvector/pgvector:pg16 его содержит.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.CreateTable(
                name: "SearchChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Embedding = table.Column<HalfVector>(type: "halfvec(512)", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    ModelVersion = table.Column<string>(type: "text", nullable: false),
                    SourceUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tsv = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('russian', \"Content\")", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchChunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchIndexQueue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    EnqueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchIndexQueue", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchChunks_BoardId_IsClosed",
                table: "SearchChunks",
                columns: new[] { "BoardId", "IsClosed" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchChunks_ContentHash",
                table: "SearchChunks",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_SearchChunks_SourceType_SourceId",
                table: "SearchChunks",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchChunks_SourceType_SourceId_ChunkIndex_ModelVersion",
                table: "SearchChunks",
                columns: new[] { "SourceType", "SourceId", "ChunkIndex", "ModelVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexQueue_Priority_NextAttemptAt",
                table: "SearchIndexQueue",
                columns: new[] { "Priority", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexQueue_SourceType_SourceId_Operation",
                table: "SearchIndexQueue",
                columns: new[] { "SourceType", "SourceId", "Operation" },
                unique: true);

            // HNSW и GIN EF не умеет — только сырым SQL. m/ef_construction — значения из ТЗ:
            // компромисс между временем построения и полнотой ANN-поиска.
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_SearchChunks_Embedding_Hnsw" ON "SearchChunks"
                USING hnsw ("Embedding" halfvec_cosine_ops) WITH (m = 16, ef_construction = 64);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_SearchChunks_Tsv" ON "SearchChunks" USING gin ("Tsv");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_SearchChunks_Tsv";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_SearchChunks_Embedding_Hnsw";""");

            migrationBuilder.DropTable(
                name: "SearchChunks");

            migrationBuilder.DropTable(
                name: "SearchIndexQueue");

            // Расширение не удаляем: его могли поставить руками или использовать другие схемы,
            // а откат миграции не повод трогать чужое.
        }
    }
}
