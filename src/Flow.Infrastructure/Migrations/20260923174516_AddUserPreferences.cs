using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrefSidebarMode",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrefStartPage",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrefTasksPageSize",
                table: "Users",
                type: "integer",
                nullable: false,
                // Существующим профилям — то же, что UserPreferences.Default: 0 не входит в допустимые размеры.
                defaultValue: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrefSidebarMode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PrefStartPage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PrefTasksPageSize",
                table: "Users");
        }
    }
}
