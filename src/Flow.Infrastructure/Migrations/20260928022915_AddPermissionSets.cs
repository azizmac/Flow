using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PermissionSetId",
                table: "BoardMembers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PermissionSetId",
                table: "BoardGroups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PermissionSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BaseRole = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Permissions = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionSets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoardMembers_PermissionSetId",
                table: "BoardMembers",
                column: "PermissionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardGroups_PermissionSetId",
                table: "BoardGroups",
                column: "PermissionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionSets_Name",
                table: "PermissionSets",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BoardGroups_PermissionSets_PermissionSetId",
                table: "BoardGroups",
                column: "PermissionSetId",
                principalTable: "PermissionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BoardMembers_PermissionSets_PermissionSetId",
                table: "BoardMembers",
                column: "PermissionSetId",
                principalTable: "PermissionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BoardGroups_PermissionSets_PermissionSetId",
                table: "BoardGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_BoardMembers_PermissionSets_PermissionSetId",
                table: "BoardMembers");

            migrationBuilder.DropTable(
                name: "PermissionSets");

            migrationBuilder.DropIndex(
                name: "IX_BoardMembers_PermissionSetId",
                table: "BoardMembers");

            migrationBuilder.DropIndex(
                name: "IX_BoardGroups_PermissionSetId",
                table: "BoardGroups");

            migrationBuilder.DropColumn(
                name: "PermissionSetId",
                table: "BoardMembers");

            migrationBuilder.DropColumn(
                name: "PermissionSetId",
                table: "BoardGroups");
        }
    }
}
