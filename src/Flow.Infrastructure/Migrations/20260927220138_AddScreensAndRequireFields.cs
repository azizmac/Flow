using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScreensAndRequireFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "RequireFields",
                table: "StatusTransitions",
                type: "uuid[]",
                nullable: false,
                // У существующих переходов обязательных полей нет — пустой массив, а не NULL.
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.CreateTable(
                name: "TaskScreens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Context = table.Column<int>(type: "integer", nullable: false),
                    Fields = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskScreens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskScreens_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskScreens_TaskTypes_TaskTypeId",
                        column: x => x.TaskTypeId,
                        principalTable: "TaskTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskScreens_BoardId_TaskTypeId_Context",
                table: "TaskScreens",
                columns: new[] { "BoardId", "TaskTypeId", "Context" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TaskScreens_TaskTypeId",
                table: "TaskScreens",
                column: "TaskTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskScreens");

            migrationBuilder.DropColumn(
                name: "RequireFields",
                table: "StatusTransitions");
        }
    }
}
