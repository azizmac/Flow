using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameScmTablesToGit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScmConnections_Users_CreatedById",
                table: "ScmConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmDeliveries_ScmRepositories_RepositoryId",
                table: "ScmDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmLinks_ScmRepositories_RepositoryId",
                table: "ScmLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmLinks_TaskItems_TaskId",
                table: "ScmLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmLinks_Users_AuthorUserId",
                table: "ScmLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmRepositories_ScmConnections_ConnectionId",
                table: "ScmRepositories");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmRepositoryBoards_Boards_BoardId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmRepositoryBoards_ScmRepositories_RepositoryId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmRepositoryBoards_Statuses_OnPullRequestMergedStatusId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmRepositoryBoards_Statuses_OnPullRequestOpenedStatusId",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_ScmRepositoryBoards_Users_CreatedById",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ScmRepositoryBoards",
                table: "ScmRepositoryBoards");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ScmRepositories",
                table: "ScmRepositories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ScmLinks",
                table: "ScmLinks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ScmDeliveries",
                table: "ScmDeliveries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ScmConnections",
                table: "ScmConnections");

            migrationBuilder.RenameTable(
                name: "ScmRepositoryBoards",
                newName: "GitRepositoryBoards");

            migrationBuilder.RenameTable(
                name: "ScmRepositories",
                newName: "GitRepositories");

            migrationBuilder.RenameTable(
                name: "ScmLinks",
                newName: "GitDevelopmentLinks");

            migrationBuilder.RenameTable(
                name: "ScmDeliveries",
                newName: "GitIntegrationJobs");

            migrationBuilder.RenameTable(
                name: "ScmConnections",
                newName: "GitHostConnections");

            migrationBuilder.RenameIndex(
                name: "IX_ScmRepositoryBoards_OnPullRequestOpenedStatusId",
                table: "GitRepositoryBoards",
                newName: "IX_GitRepositoryBoards_OnPullRequestOpenedStatusId");

            migrationBuilder.RenameIndex(
                name: "IX_ScmRepositoryBoards_OnPullRequestMergedStatusId",
                table: "GitRepositoryBoards",
                newName: "IX_GitRepositoryBoards_OnPullRequestMergedStatusId");

            migrationBuilder.RenameIndex(
                name: "IX_ScmRepositoryBoards_CreatedById",
                table: "GitRepositoryBoards",
                newName: "IX_GitRepositoryBoards_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_ScmRepositoryBoards_BoardId",
                table: "GitRepositoryBoards",
                newName: "IX_GitRepositoryBoards_BoardId");

            migrationBuilder.RenameIndex(
                name: "IX_ScmRepositories_ConnectionId_ExternalId",
                table: "GitRepositories",
                newName: "IX_GitRepositories_ConnectionId_ExternalId");

            migrationBuilder.RenameIndex(
                name: "IX_ScmLinks_TaskId_RepositoryId_Kind_ExternalId",
                table: "GitDevelopmentLinks",
                newName: "IX_GitDevelopmentLinks_TaskId_RepositoryId_Kind_ExternalId");

            migrationBuilder.RenameIndex(
                name: "IX_ScmLinks_RepositoryId_Kind_ExternalId",
                table: "GitDevelopmentLinks",
                newName: "IX_GitDevelopmentLinks_RepositoryId_Kind_ExternalId");

            migrationBuilder.RenameIndex(
                name: "IX_ScmLinks_AuthorUserId",
                table: "GitDevelopmentLinks",
                newName: "IX_GitDevelopmentLinks_AuthorUserId");

            migrationBuilder.RenameIndex(
                name: "IX_ScmDeliveries_Status_NextAttemptAt",
                table: "GitIntegrationJobs",
                newName: "IX_GitIntegrationJobs_Status_NextAttemptAt");

            migrationBuilder.RenameIndex(
                name: "IX_ScmDeliveries_RepositoryId_DeliveryId",
                table: "GitIntegrationJobs",
                newName: "IX_GitIntegrationJobs_RepositoryId_DeliveryId");

            migrationBuilder.RenameIndex(
                name: "IX_ScmConnections_CreatedById",
                table: "GitHostConnections",
                newName: "IX_GitHostConnections_CreatedById");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GitRepositoryBoards",
                table: "GitRepositoryBoards",
                columns: new[] { "RepositoryId", "BoardId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_GitRepositories",
                table: "GitRepositories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GitDevelopmentLinks",
                table: "GitDevelopmentLinks",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GitIntegrationJobs",
                table: "GitIntegrationJobs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GitHostConnections",
                table: "GitHostConnections",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GitDevelopmentLinks_GitRepositories_RepositoryId",
                table: "GitDevelopmentLinks",
                column: "RepositoryId",
                principalTable: "GitRepositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GitDevelopmentLinks_TaskItems_TaskId",
                table: "GitDevelopmentLinks",
                column: "TaskId",
                principalTable: "TaskItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GitDevelopmentLinks_Users_AuthorUserId",
                table: "GitDevelopmentLinks",
                column: "AuthorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GitHostConnections_Users_CreatedById",
                table: "GitHostConnections",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GitIntegrationJobs_GitRepositories_RepositoryId",
                table: "GitIntegrationJobs",
                column: "RepositoryId",
                principalTable: "GitRepositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GitRepositories_GitHostConnections_ConnectionId",
                table: "GitRepositories",
                column: "ConnectionId",
                principalTable: "GitHostConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GitRepositoryBoards_Boards_BoardId",
                table: "GitRepositoryBoards",
                column: "BoardId",
                principalTable: "Boards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GitRepositoryBoards_GitRepositories_RepositoryId",
                table: "GitRepositoryBoards",
                column: "RepositoryId",
                principalTable: "GitRepositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GitRepositoryBoards_Statuses_OnPullRequestMergedStatusId",
                table: "GitRepositoryBoards",
                column: "OnPullRequestMergedStatusId",
                principalTable: "Statuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GitRepositoryBoards_Statuses_OnPullRequestOpenedStatusId",
                table: "GitRepositoryBoards",
                column: "OnPullRequestOpenedStatusId",
                principalTable: "Statuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GitRepositoryBoards_Users_CreatedById",
                table: "GitRepositoryBoards",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GitDevelopmentLinks_GitRepositories_RepositoryId",
                table: "GitDevelopmentLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_GitDevelopmentLinks_TaskItems_TaskId",
                table: "GitDevelopmentLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_GitDevelopmentLinks_Users_AuthorUserId",
                table: "GitDevelopmentLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_GitHostConnections_Users_CreatedById",
                table: "GitHostConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_GitIntegrationJobs_GitRepositories_RepositoryId",
                table: "GitIntegrationJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_GitRepositories_GitHostConnections_ConnectionId",
                table: "GitRepositories");

            migrationBuilder.DropForeignKey(
                name: "FK_GitRepositoryBoards_Boards_BoardId",
                table: "GitRepositoryBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_GitRepositoryBoards_GitRepositories_RepositoryId",
                table: "GitRepositoryBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_GitRepositoryBoards_Statuses_OnPullRequestMergedStatusId",
                table: "GitRepositoryBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_GitRepositoryBoards_Statuses_OnPullRequestOpenedStatusId",
                table: "GitRepositoryBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_GitRepositoryBoards_Users_CreatedById",
                table: "GitRepositoryBoards");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GitRepositoryBoards",
                table: "GitRepositoryBoards");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GitRepositories",
                table: "GitRepositories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GitIntegrationJobs",
                table: "GitIntegrationJobs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GitHostConnections",
                table: "GitHostConnections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GitDevelopmentLinks",
                table: "GitDevelopmentLinks");

            migrationBuilder.RenameTable(
                name: "GitRepositoryBoards",
                newName: "ScmRepositoryBoards");

            migrationBuilder.RenameTable(
                name: "GitRepositories",
                newName: "ScmRepositories");

            migrationBuilder.RenameTable(
                name: "GitIntegrationJobs",
                newName: "ScmDeliveries");

            migrationBuilder.RenameTable(
                name: "GitHostConnections",
                newName: "ScmConnections");

            migrationBuilder.RenameTable(
                name: "GitDevelopmentLinks",
                newName: "ScmLinks");

            migrationBuilder.RenameIndex(
                name: "IX_GitRepositoryBoards_OnPullRequestOpenedStatusId",
                table: "ScmRepositoryBoards",
                newName: "IX_ScmRepositoryBoards_OnPullRequestOpenedStatusId");

            migrationBuilder.RenameIndex(
                name: "IX_GitRepositoryBoards_OnPullRequestMergedStatusId",
                table: "ScmRepositoryBoards",
                newName: "IX_ScmRepositoryBoards_OnPullRequestMergedStatusId");

            migrationBuilder.RenameIndex(
                name: "IX_GitRepositoryBoards_CreatedById",
                table: "ScmRepositoryBoards",
                newName: "IX_ScmRepositoryBoards_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_GitRepositoryBoards_BoardId",
                table: "ScmRepositoryBoards",
                newName: "IX_ScmRepositoryBoards_BoardId");

            migrationBuilder.RenameIndex(
                name: "IX_GitRepositories_ConnectionId_ExternalId",
                table: "ScmRepositories",
                newName: "IX_ScmRepositories_ConnectionId_ExternalId");

            migrationBuilder.RenameIndex(
                name: "IX_GitIntegrationJobs_Status_NextAttemptAt",
                table: "ScmDeliveries",
                newName: "IX_ScmDeliveries_Status_NextAttemptAt");

            migrationBuilder.RenameIndex(
                name: "IX_GitIntegrationJobs_RepositoryId_DeliveryId",
                table: "ScmDeliveries",
                newName: "IX_ScmDeliveries_RepositoryId_DeliveryId");

            migrationBuilder.RenameIndex(
                name: "IX_GitHostConnections_CreatedById",
                table: "ScmConnections",
                newName: "IX_ScmConnections_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_GitDevelopmentLinks_TaskId_RepositoryId_Kind_ExternalId",
                table: "ScmLinks",
                newName: "IX_ScmLinks_TaskId_RepositoryId_Kind_ExternalId");

            migrationBuilder.RenameIndex(
                name: "IX_GitDevelopmentLinks_RepositoryId_Kind_ExternalId",
                table: "ScmLinks",
                newName: "IX_ScmLinks_RepositoryId_Kind_ExternalId");

            migrationBuilder.RenameIndex(
                name: "IX_GitDevelopmentLinks_AuthorUserId",
                table: "ScmLinks",
                newName: "IX_ScmLinks_AuthorUserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScmRepositoryBoards",
                table: "ScmRepositoryBoards",
                columns: new[] { "RepositoryId", "BoardId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScmRepositories",
                table: "ScmRepositories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScmDeliveries",
                table: "ScmDeliveries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScmConnections",
                table: "ScmConnections",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScmLinks",
                table: "ScmLinks",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ScmConnections_Users_CreatedById",
                table: "ScmConnections",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScmDeliveries_ScmRepositories_RepositoryId",
                table: "ScmDeliveries",
                column: "RepositoryId",
                principalTable: "ScmRepositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ScmLinks_ScmRepositories_RepositoryId",
                table: "ScmLinks",
                column: "RepositoryId",
                principalTable: "ScmRepositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ScmLinks_TaskItems_TaskId",
                table: "ScmLinks",
                column: "TaskId",
                principalTable: "TaskItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ScmLinks_Users_AuthorUserId",
                table: "ScmLinks",
                column: "AuthorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ScmRepositories_ScmConnections_ConnectionId",
                table: "ScmRepositories",
                column: "ConnectionId",
                principalTable: "ScmConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ScmRepositoryBoards_Boards_BoardId",
                table: "ScmRepositoryBoards",
                column: "BoardId",
                principalTable: "Boards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ScmRepositoryBoards_ScmRepositories_RepositoryId",
                table: "ScmRepositoryBoards",
                column: "RepositoryId",
                principalTable: "ScmRepositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

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

            migrationBuilder.AddForeignKey(
                name: "FK_ScmRepositoryBoards_Users_CreatedById",
                table: "ScmRepositoryBoards",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
