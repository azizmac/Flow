using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGitRepositoryWorkspaceSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastSyncError",
                table: "ScmRepositories",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Краткая причина неудачной синхронизации.");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "ScmRepositories",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Время последней успешной синхронизации.");

            migrationBuilder.AddColumn<string>(
                name: "LastSyncedCommit",
                table: "ScmRepositories",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Commit последней успешной синхронизации.");

            migrationBuilder.AddColumn<int>(
                name: "SyncState",
                table: "ScmRepositories",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Состояние локальной копии репозитория.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSyncError",
                table: "ScmRepositories");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "ScmRepositories");

            migrationBuilder.DropColumn(
                name: "LastSyncedCommit",
                table: "ScmRepositories");

            migrationBuilder.DropColumn(
                name: "SyncState",
                table: "ScmRepositories");
        }
    }
}
