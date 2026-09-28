using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskHierarchyAndRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskItems_BoardId",
                table: "TaskItems");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "TaskItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rank",
                table: "TaskItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                collation: "C");

            // Ранги существующим задачам — по времени создания внутри проекта (docs/TZ_task_model.md §7).
            // Ключи — целая часть дробного индекса «d» + 4 цифры base-62 (до 14 млн задач на проект): так же,
            // как FractionalIndex.Between(prev, null) растил бы их при добавлении в конец, и новые задачи
            // после миграции встанут следом (d0006 → d0007). Номер строки — с единицы: «d0000» тоже валиден,
            // но так ключи совпадают с номерами строк в выборке.
            migrationBuilder.Sql("""
                UPDATE "TaskItems" t SET "Rank" = r."Rank"
                FROM (
                    SELECT "Id", 'd'
                        || substr(a, ((n / 238328) % 62)::int + 1, 1)
                        || substr(a, ((n / 3844) % 62)::int + 1, 1)
                        || substr(a, ((n / 62) % 62)::int + 1, 1)
                        || substr(a, (n % 62)::int + 1, 1) AS "Rank"
                    FROM (
                        SELECT "Id",
                               row_number() OVER (PARTITION BY "BoardId" ORDER BY "CreatedAt", "Id") AS n,
                               '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz' AS a
                        FROM "TaskItems") x
                ) r
                WHERE t."Id" = r."Id";
                """);

            // Пустая строка по умолчанию была нужна только для ADD COLUMN NOT NULL; невалидный ключ в БД не оставляем.
            migrationBuilder.Sql("""ALTER TABLE "TaskItems" ALTER COLUMN "Rank" DROP DEFAULT;""");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_BoardId_Rank",
                table: "TaskItems",
                columns: new[] { "BoardId", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ParentId",
                table: "TaskItems",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_TaskItems_ParentId",
                table: "TaskItems",
                column: "ParentId",
                principalTable: "TaskItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_TaskItems_ParentId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_BoardId_Rank",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ParentId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "TaskItems");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_BoardId",
                table: "TaskItems",
                column: "BoardId");
        }
    }
}
