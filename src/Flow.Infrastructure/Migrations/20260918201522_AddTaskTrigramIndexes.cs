using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTrigramIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SearchIndexQueue_Priority_NextAttemptAt",
                table: "SearchIndexQueue");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexQueue_Priority_EnqueuedAt",
                table: "SearchIndexQueue",
                columns: new[] { "Priority", "EnqueuedAt" });

            // Текстовый фильтр списка задач — ILIKE '%…%' по Title и Code (TaskItemRepository.Filtered).
            // Оба шаблона начинаются с %, поэтому ни уникальный btree по Code, ни любой другой btree
            // тут неприменимы принципиально: без триграмм это seq scan по всей таблице, причём трижды
            // за запрос (выборка + два CountAsync). gin_trgm_ops — единственный индекс, который ILIKE
            // с ведущим % умеет использовать.
            //
            // Расширение ставится миграцией по тем же причинам, что и vector в AddSearchIndex:
            // docker/postgres/init.sql выполняется только при первичной инициализации тома.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_TaskItems_Title_Trgm" ON "TaskItems" USING gin ("Title" gin_trgm_ops);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_TaskItems_Code_Trgm" ON "TaskItems" USING gin ("Code" gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Расширение не снимаем: его может использовать что-то ещё в этой же базе.
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_TaskItems_Code_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_TaskItems_Title_Trgm";""");

            migrationBuilder.DropIndex(
                name: "IX_SearchIndexQueue_Priority_EnqueuedAt",
                table: "SearchIndexQueue");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexQueue_Priority_NextAttemptAt",
                table: "SearchIndexQueue",
                columns: new[] { "Priority", "NextAttemptAt" });
        }
    }
}
