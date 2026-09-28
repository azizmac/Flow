using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScmGitHubApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AppId",
                table: "ScmConnections",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthKind",
                table: "ScmConnections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "InstallationId",
                table: "ScmConnections",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppId",
                table: "ScmConnections");

            migrationBuilder.DropColumn(
                name: "AuthKind",
                table: "ScmConnections");

            migrationBuilder.DropColumn(
                name: "InstallationId",
                table: "ScmConnections");
        }
    }
}
