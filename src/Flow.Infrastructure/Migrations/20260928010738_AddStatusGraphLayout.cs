using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusGraphLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "GraphX",
                table: "Statuses",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GraphY",
                table: "Statuses",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GraphX",
                table: "Statuses");

            migrationBuilder.DropColumn(
                name: "GraphY",
                table: "Statuses");
        }
    }
}
