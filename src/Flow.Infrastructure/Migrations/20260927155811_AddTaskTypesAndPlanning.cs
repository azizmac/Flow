using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTypesAndPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Up переписан вручную (как AddUserRoleAndStatus): EF предлагал TypeId NOT NULL с пустым Guid
            // и UpdatedAt = 0001-01-01 — FK на TaskTypes такое не пропустит, а сортировка «по изменению» соврала бы.
            // Порядок: колонки nullable → таблица типов → типы по умолчанию каждому проекту → перенос данных →
            // NOT NULL → индекс и FK.
            migrationBuilder.AddColumn<int>(
                name: "EstimateMinutes",
                table: "TaskItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "TaskItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "TaskItems",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StoryPoints",
                table: "TaskItems",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TypeId",
                table: "TaskItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "TaskItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskTypes_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypes_BoardId_Name",
                table: "TaskTypes",
                columns: new[] { "BoardId", "Name" },
                unique: true);

            // Тот же набор, что DefaultTaskTypes.All (Kind — число TaskTypeKind): существующие проекты получают
            // ровно то, что получил бы новый. Все задачи встают на тип по умолчанию — «Задача».
            migrationBuilder.Sql("""
                INSERT INTO "TaskTypes" ("Id", "BoardId", "Name", "Kind", "SortOrder", "IsDefault", "IsArchived")
                SELECT gen_random_uuid(), b."Id", t.name, t.kind, t.sort_order, t.is_default, false
                FROM "Boards" b
                CROSS JOIN (VALUES
                    ('Эпик', 0, 0, false),
                    ('История', 1, 1, false),
                    ('Задача', 2, 2, true),
                    ('Ошибка', 3, 3, false),
                    ('Подзадача', 4, 4, false)
                ) AS t(name, kind, sort_order, is_default);

                UPDATE "TaskItems" ti
                SET "TypeId" = tt."Id"
                FROM "TaskTypes" tt
                WHERE tt."BoardId" = ti."BoardId" AND tt."IsDefault";

                UPDATE "TaskItems" SET "UpdatedAt" = "CreatedAt";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "TypeId",
                table: "TaskItems",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "TaskItems",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_TypeId",
                table: "TaskItems",
                column: "TypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_TaskTypes_TypeId",
                table: "TaskItems",
                column: "TypeId",
                principalTable: "TaskTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_TaskTypes_TypeId",
                table: "TaskItems");

            migrationBuilder.DropTable(
                name: "TaskTypes");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_TypeId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "EstimateMinutes",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "StoryPoints",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "TypeId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "TaskItems");
        }
    }
}
