using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScmPullRequestComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CommentOnPullRequests",
                table: "ScmRepositoryBoards",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommentOnPullRequests",
                table: "ScmRepositoryBoards");
        }
    }
}
