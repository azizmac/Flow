using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScmAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "TaskActivities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "TaskActivities",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnPullRequestMergedStatusId",
                table: "ScmRepositoryBoards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnPullRequestOpenedStatusId",
                table: "ScmRepositoryBoards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmartCommits",
                table: "ScmRepositoryBoards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "ScmLinks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScmRepositoryBoards_OnPullRequestMergedStatusId",
                table: "ScmRepositoryBoards",
                column: "OnPullRequestMergedStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_ScmRepositoryBoards_OnPullRequestOpenedStatusId",
                table: "ScmRepositoryBoards",
                column: "OnPullRequestOpenedStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScmRepositoryBoards_Statuses_OnPullRequestMergedStatusId",
                table: "ScmRepositoryBoards",
                column: "OnPullRequestMergedStatusId",
                principalTable: "Statuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ScmRepositoryBoards_Statuses_OnPullRequestOpenedStatusId",
                table: "ScmRepositoryBoards",
                column: "OnPullRequestOpenedStatusId",
                principalTable: "Statuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScmRepositoryBoards_Statuses_OnPullRequestMergedStatusId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmRepositoryBoards_Statuses_OnPullRequestOpenedStatusId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropIndex(
                name: "IX_ScmRepositoryBoards_OnPullRequestMergedStatusId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropIndex(
                name: "IX_ScmRepositoryBoards_OnPullRequestOpenedStatusId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "TaskActivities");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "TaskActivities");

            migrationBuilder.DropColumn(
                name: "OnPullRequestMergedStatusId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropColumn(
                name: "OnPullRequestOpenedStatusId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropColumn(
                name: "SmartCommits",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "ScmLinks");
        }
    }
}
