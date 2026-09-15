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
            // Расширение должно существовать до таблицы: без него тип halfvec неизвестен.
            // IF NOT EXISTS — миграция накатывается и на базу, где расширение уже включили руками.
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
                    // Лексический вектор считает сама БД: так он не может разойтись с Content.
                    Tsv = table.Column<NpgsqlTsVector>(
                        type: "tsvector",
                        nullable: true,
                        computedColumnSql: "to_tsvector('russian', \"Content\")",
                        stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchChunks", x => x.Id);
                });

            // Внешних ключей на TaskItems/TaskComments/Boards/Users нет намеренно: индекс — проекция,
            // он догоняет источники асинхронно и не должен мешать их удалять. Чистит его воркер.
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
                name: "IX_SearchChunks_ContentHash",
                table: "SearchChunks",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_SearchChunks_BoardId_IsClosed",
                table: "SearchChunks",
                columns: new[] { "BoardId", "IsClosed" });

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

            // HNSW и GIN EF не выражает — сырым SQL. m/ef_construction: значения по умолчанию pgvector,
            // их стоит поднимать, когда чанков станут миллионы и recall перестанет устраивать.
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_SearchChunks_Embedding_Hnsw"
                ON "SearchChunks" USING hnsw ("Embedding" halfvec_cosine_ops)
                WITH (m = 16, ef_construction = 64);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_SearchChunks_Tsv_Gin"
                ON "SearchChunks" USING gin ("Tsv");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_SearchChunks_Tsv_Gin\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_SearchChunks_Embedding_Hnsw\";");

            migrationBuilder.DropTable(
                name: "SearchIndexQueue");

            migrationBuilder.DropTable(
                name: "SearchChunks");

            // Расширение не удаляем: его могли включить до нас и им могут пользоваться другие схемы,
            // а DROP EXTENSION унёс бы их типы вместе с данными.
        }
    }
}
