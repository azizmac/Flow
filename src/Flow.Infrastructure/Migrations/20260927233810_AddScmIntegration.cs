using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScmIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScmConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SecretProtected = table.Column<string>(type: "text", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckedLogin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScmConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScmConnections_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScmRepositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FullName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    WebUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DefaultBranch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    WebhookId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WebhookSecretProtected = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastDeliveryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScmRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScmRepositories_ScmConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "ScmConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScmDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Event = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScmDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScmDeliveries_ScmRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "ScmRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScmLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: true),
                    AuthorLogin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBranch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TargetBranch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScmLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScmLinks_ScmRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "ScmRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScmLinks_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScmLinks_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ScmRepositoryBoards",
                columns: table => new
                {
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScmRepositoryBoards", x => new { x.RepositoryId, x.BoardId });
                    table.ForeignKey(
                        name: "FK_ScmRepositoryBoards_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScmRepositoryBoards_ScmRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "ScmRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScmRepositoryBoards_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScmConnections_CreatedById",
                table: "ScmConnections",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ScmDeliveries_RepositoryId_DeliveryId",
                table: "ScmDeliveries",
                columns: new[] { "RepositoryId", "DeliveryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScmDeliveries_Status_NextAttemptAt",
                table: "ScmDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScmLinks_AuthorUserId",
                table: "ScmLinks",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScmLinks_RepositoryId_Kind_ExternalId",
                table: "ScmLinks",
                columns: new[] { "RepositoryId", "Kind", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScmLinks_TaskId_RepositoryId_Kind_ExternalId",
                table: "ScmLinks",
                columns: new[] { "TaskId", "RepositoryId", "Kind", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScmRepositories_ConnectionId_ExternalId",
                table: "ScmRepositories",
                columns: new[] { "ConnectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScmRepositoryBoards_BoardId",
                table: "ScmRepositoryBoards",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_ScmRepositoryBoards_CreatedById",
                table: "ScmRepositoryBoards",
                column: "CreatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScmDeliveries");

            migrationBuilder.DropTable(
                name: "ScmLinks");

            migrationBuilder.DropTable(
                name: "ScmRepositoryBoards");

            migrationBuilder.DropTable(
                name: "ScmRepositories");

            migrationBuilder.DropTable(
                name: "ScmConnections");
        }
    }
}
