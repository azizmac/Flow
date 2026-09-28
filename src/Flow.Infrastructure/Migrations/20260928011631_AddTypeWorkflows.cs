using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTypeWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StatusTransitions_BoardId_FromStatusId_ToStatusId",
                table: "StatusTransitions");

            migrationBuilder.AddColumn<int>(
                name: "OwnWorkflowMode",
                table: "TaskTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaskTypeId",
                table: "StatusTransitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatusTransitions_BoardId_TaskTypeId_FromStatusId_ToStatusId",
                table: "StatusTransitions",
                columns: new[] { "BoardId", "TaskTypeId", "FromStatusId", "ToStatusId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_StatusTransitions_TaskTypeId",
                table: "StatusTransitions",
                column: "TaskTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_StatusTransitions_TaskTypes_TaskTypeId",
                table: "StatusTransitions",
                column: "TaskTypeId",
                principalTable: "TaskTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StatusTransitions_TaskTypes_TaskTypeId",
                table: "StatusTransitions");

            migrationBuilder.DropIndex(
                name: "IX_StatusTransitions_BoardId_TaskTypeId_FromStatusId_ToStatusId",
                table: "StatusTransitions");

            migrationBuilder.DropIndex(
                name: "IX_StatusTransitions_TaskTypeId",
                table: "StatusTransitions");

            migrationBuilder.DropColumn(
                name: "OwnWorkflowMode",
                table: "TaskTypes");

            migrationBuilder.DropColumn(
                name: "TaskTypeId",
                table: "StatusTransitions");

            migrationBuilder.CreateIndex(
                name: "IX_StatusTransitions_BoardId_FromStatusId_ToStatusId",
                table: "StatusTransitions",
                columns: new[] { "BoardId", "FromStatusId", "ToStatusId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
