using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MilestoneId",
                table: "TaskItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Milestones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Milestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Milestones_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_MilestoneId",
                table: "TaskItems",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_Milestones_BoardId_Name",
                table: "Milestones",
                columns: new[] { "BoardId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Milestones_BoardId_SortOrder",
                table: "Milestones",
                columns: new[] { "BoardId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Milestones_MilestoneId",
                table: "TaskItems",
                column: "MilestoneId",
                principalTable: "Milestones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Milestones_MilestoneId",
                table: "TaskItems");

            migrationBuilder.DropTable(
                name: "Milestones");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_MilestoneId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "MilestoneId",
                table: "TaskItems");
        }
    }
}
