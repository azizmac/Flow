using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <summary>
    /// Роли и статусы пользователя (docs/TZ_user_roles.md, #17). Порядок ручной: сначала новые колонки и перенос данных
    /// из IsActive/DeactivatedAt, потом удаление старых — сгенерированный вариант ронял IsActive до переноса.
    /// Данные: Status = Deactivated (2), где IsActive = false, иначе Active (1); StatusChangedAt = DeactivatedAt;
    /// Role = Member (1) всем, самому раннему по CreatedAt — Owner (4).
    /// </summary>
    public partial class AddUserRoleAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("""
                UPDATE "Users" SET "Status" = CASE WHEN "IsActive" THEN 1 ELSE 2 END;
                UPDATE "Users" SET "Role" = 4
                WHERE "Id" = (SELECT "Id" FROM "Users" ORDER BY "CreatedAt", "Id" LIMIT 1);
                """);

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "DeactivatedAt",
                table: "Users",
                newName: "StatusChangedAt");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "TaskItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_CreatedById",
                table: "TaskItems",
                column: "CreatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Users_CreatedById",
                table: "TaskItems",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Users_CreatedById",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_CreatedById",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "TaskItems");

            migrationBuilder.RenameColumn(
                name: "StatusChangedAt",
                table: "Users",
                newName: "DeactivatedAt");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""
                UPDATE "Users" SET "IsActive" = ("Status" <> 2);
                """);

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Users");
        }
    }
}
