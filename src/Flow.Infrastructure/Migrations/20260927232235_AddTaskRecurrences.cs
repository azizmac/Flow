using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRecurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskRecurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LeadDays = table.Column<int>(type: "integer", nullable: false),
                    DueOffsetDays = table.Column<int>(type: "integer", nullable: true),
                    CopyAssignee = table.Column<bool>(type: "boolean", nullable: false),
                    CopyChecklist = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RuleFrequency = table.Column<int>(type: "integer", nullable: false),
                    RuleInterval = table.Column<int>(type: "integer", nullable: false),
                    RuleMonthDay = table.Column<int>(type: "integer", nullable: true),
                    RuleWeekDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRecurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskRecurrences_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskRecurrences_TaskItems_TemplateTaskId",
                        column: x => x.TemplateTaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskRecurrences_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskRecurrenceOccurrences",
                columns: table => new
                {
                    RecurrenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccursOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRecurrenceOccurrences", x => new { x.RecurrenceId, x.OccursOn });
                    table.ForeignKey(
                        name: "FK_TaskRecurrenceOccurrences_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TaskRecurrenceOccurrences_TaskRecurrences_RecurrenceId",
                        column: x => x.RecurrenceId,
                        principalTable: "TaskRecurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRecurrenceOccurrences_TaskId",
                table: "TaskRecurrenceOccurrences",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskRecurrences_BoardId",
                table: "TaskRecurrences",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskRecurrences_CreatedById",
                table: "TaskRecurrences",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TaskRecurrences_IsActive",
                table: "TaskRecurrences",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TaskRecurrences_TemplateTaskId",
                table: "TaskRecurrences",
                column: "TemplateTaskId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskRecurrenceOccurrences");

            migrationBuilder.DropTable(
                name: "TaskRecurrences");
        }
    }
}
