using System;
using System.Collections.Generic;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomFields",
                table: "TaskItems",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.CreateTable(
                name: "CustomFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    TaskTypeIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Options = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomFields_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFields_BoardId_Key",
                table: "CustomFields",
                columns: new[] { "BoardId", "Key" },
                unique: true);

            // Поиск по значениям (FQL cf.*, будущие индексы по выражению) — GIN с jsonb_path_ops, как в ТЗ.
            migrationBuilder.Sql("""CREATE INDEX "IX_TaskItems_CustomFields" ON "TaskItems" USING GIN ("CustomFields" jsonb_path_ops);""");
            migrationBuilder.Sql(CustomFieldSql.CreateFunctionsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CustomFieldSql.DropFunctionsSql);
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_TaskItems_CustomFields";""");

            migrationBuilder.DropTable(
                name: "CustomFields");

            migrationBuilder.DropColumn(
                name: "CustomFields",
                table: "TaskItems");
        }
    }
}
