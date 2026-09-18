using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeRepositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Идентификатор подключённого Git-репозитория."),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Идентификатор проекта Flow, которому принадлежит репозиторий."),
                    Provider = table.Column<int>(type: "integer", nullable: false, comment: "Git-провайдер: GitHub или GitLab."),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Понятное пользователю имя репозитория."),
                    RemoteUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "HTTPS-адрес Git remote без учётных данных и параметров."),
                    Branch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, comment: "Ветка репозитория."),
                    SyncState = table.Column<int>(type: "integer", nullable: false, comment: "Текущее состояние синхронизации локальной копии репозитория."),
                    LastSyncedCommit = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Commit последней успешной синхронизации; сохраняется после неудачного обновления."),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "Время последней успешной синхронизации."),
                    LastSyncError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Краткая безопасная причина последней неудачи синхронизации."),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "Время подключения репозитория к проекту.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeRepositories_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRepositories_BoardId",
                table: "CodeRepositories",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeRepositories_BoardId_RemoteUrl",
                table: "CodeRepositories",
                columns: new[] { "BoardId", "RemoteUrl" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeRepositories");
        }
    }
}
