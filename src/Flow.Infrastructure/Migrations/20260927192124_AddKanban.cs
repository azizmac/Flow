using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKanban : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrefTasksView",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAt",
                table: "TaskItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Время смены статуса — из журнала (последний StatusChanged = 3), без записей — дата создания: иначе все
            // старые закрытые задачи попали бы в окно финальной колонки или выпали бы из него разом.
            migrationBuilder.Sql("""
                UPDATE "TaskItems" t
                SET "StatusChangedAt" = COALESCE(
                    (SELECT max(a."CreatedAt") FROM "TaskActivities" a WHERE a."TaskId" = t."Id" AND a."Type" = 3),
                    t."CreatedAt");
                """);

            migrationBuilder.AddColumn<int>(
                name: "WipLimit",
                table: "Statuses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DoneColumnDays",
                table: "Boards",
                type: "integer",
                nullable: false,
                defaultValue: 14);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrefTasksView",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "WipLimit",
                table: "Statuses");

            migrationBuilder.DropColumn(
                name: "DoneColumnDays",
                table: "Boards");
        }
    }
}
