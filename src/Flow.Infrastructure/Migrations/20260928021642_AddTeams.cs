using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "TaskItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTeam",
                table: "Groups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_TeamId",
                table: "TaskItems",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Groups_TeamId",
                table: "TaskItems",
                column: "TeamId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Groups_TeamId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_TeamId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "IsTeam",
                table: "Groups");
        }
    }
}
